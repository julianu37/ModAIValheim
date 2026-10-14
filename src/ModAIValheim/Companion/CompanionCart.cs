using BepInEx.Configuration;
using ModAIValheim.Companion.Jobs;
using UnityEngine;

namespace ModAIValheim.Companion
{
    /// <summary>
    /// <b>A carroça</b> (Fase 2.18, seção <c>[Carroca]</c> do <c>.cfg</c>, comando <c>modia_cart</c>).
    ///
    /// <para>
    /// No Valheim a carroça é o <c>Vagon</c>: um <c>Rigidbody</c> com rodas que o jogador puxa
    /// <b>prendendo o próprio corpo nela</b> — <c>Vagon.Interact(humanoid, hold: false, alt: false)</c>
    /// (no <c>Vagon.Update</c> do dono do ZDO) cria um <c>ConfigurableJoint</c> entre a carroça e o
    /// <c>Rigidbody</c> do personagem (<c>AttachTo</c>), desde que ele esteja a menos de
    /// <c>m_detachDistance</c> (2 m) do <c>m_attachPoint</c> e sem estar no meio de um teleporte/dodge
    /// (<c>CanAttach</c>). Chamar de novo <c>Interact</c> com ela presa <b>solta</b> (<c>Detach</c>).
    /// </para>
    ///
    /// <para>
    /// <b>O que fazemos.</b> O companheiro <b>anda até o engate</b> da carroça
    /// (<c>m_attachPoint.position - m_attachOffset</c>, <see cref="MovementOwner.Cart"/>) e, ao
    /// chegar, chama o <c>Interact</c> do jogo — a carroça vem atrás dele pelo <c>ConfigurableJoint</c>,
    /// <b>como um jogador puxando</b>. Depois de engatado ele volta a ser dirigido pelo <b>follow
    /// nativo</b> (é o que faz a carroça acompanhar você); a camada aqui só vigia (carroça destruída,
    /// junta arrebentada) e atende o <c>modia_cart release</c>.
    /// </para>
    ///
    /// <para>
    /// Adaptado (lógica) de <c>src/Companions/AI/CompanionAI.cs</c> (<c>SetPendingCart</c>/
    /// <c>CancelPendingCart</c> e o passo <c>PendingCartAttach</c> — andar até
    /// <c>m_attachPoint - m_attachOffset</c>, virar para a carroça, <c>Interact</c> e devolver o follow
    /// ao jogador) e de <c>src/Companions/UI/CompanionInteract.cs</c> (<c>DirectCart</c>: usar a carroça
    /// <b>livre</b> mais próxima, soltar quando já está presa) do <i>Offline Companions Reborn</i>
    /// (MIT — ver <c>THIRD_PARTY_NOTICES.md</c>): lá o pedido vem do menu radial/interação e vive num
    /// campo do <c>CompanionAI</c>; aqui o pedido é o comando <c>modia_cart</c>, a máquina de estados é
    /// estática e o alvo é sempre a carroça livre mais próxima — sem cópia de arquivo.
    /// </para>
    ///
    /// <para>
    /// <b>Fora do núcleo (ficou de fora):</b> o <b>engate automático</b> (ele nunca se prende a uma
    /// carroça sozinho — precisa do <c>modia_cart pull</c>), puxar com <b>força própria</b> (quem puxa
    /// é a física do jogo: o <c>ConfigurableJoint</c>, com o <c>m_playerExtraPullMass</c> do
    /// <c>Vagon</c>) e o <b>transporte da carroça por portal</b> (a carroça é um objeto físico com
    /// carga: atravessar um portal continua sendo coisa de jogador — se ela ficar para trás, a junta
    /// arrebenta e o companheiro segue você).
    /// </para>
    /// </summary>
    internal static class CompanionCart
    {
        /// <summary>Estado da camada da carroça.</summary>
        private enum CartState
        {
            /// <summary>Sem carroça.</summary>
            None,

            /// <summary>Andando até o engate da carroça.</summary>
            WalkToCart,

            /// <summary>Engatado (a carroça vem atrás pelo joint do jogo).</summary>
            Attached,
        }

        /// <summary>Teto (s) para chegar ao engate antes de desistir.</summary>
        private const float AttachTimeout = 20f;

        /// <summary>Distância (m) do engate em que ele já se prende (o jogo exige &lt; 2 m).</summary>
        private const float AttachReach = 1.6f;

        /// <summary>Quantos colliders a varredura de carroça examina por vez.</summary>
        private const int ScanBufferSize = 24;

        private static readonly Collider[] s_colliderBuffer = new Collider[ScanBufferSize];

        private static ConfigEntry<bool> _enabled;
        private static ConfigEntry<float> _radius;

        private static CartState _state = CartState.None;
        private static Vagon _cart;
        private static float _deadline;

        /// <summary>Até quando toleramos o <c>Vagon</c> ainda não ter prendido (ele prende no Update dele).</summary>
        private static float _attachGraceUntil;

        /// <summary>O <c>Vagon</c> já confirmou que o companheiro está preso (junta criada).</summary>
        private static bool _attachConfirmed;

        private static int s_vehicleMask = -1;

        internal static void Configure(ConfigEntry<bool> enabled, ConfigEntry<float> radius)
        {
            _enabled = enabled;
            _radius = radius;

            Plugin.Log.LogInfo(
                $"[ModIA] Carroca: ligada = {Enabled} | procura a carroca num raio de {Radius:0} m " +
                "(modia_cart pull|release).");
        }

        /// <summary>Ligada na config (<c>Carroca/Ligado</c>).</summary>
        internal static bool Enabled => _enabled == null || _enabled.Value;

        /// <summary>Raio (m) em que procuramos a carroça (config <c>Carroca/Raio</c>).</summary>
        private static float Radius => _radius != null ? Mathf.Clamp(_radius.Value, 3f, 80f) : 25f;

        /// <summary>O companheiro está <b>puxando</b> a carroça agora.</summary>
        internal static bool Attached => _state == CartState.Attached && _cart != null;

        /// <summary>O companheiro está indo até o engate da carroça.</summary>
        internal static bool Busy => _state != CartState.None;

        /// <summary>Solta o estado estático (unload do plugin / fim do mundo).</summary>
        internal static void Shutdown()
        {
            _state = CartState.None;
            _cart = null;
        }

        // ---------------------------------------------------------------------
        // Pedidos (modia_cart)
        // ---------------------------------------------------------------------

        /// <summary>
        /// <c>modia_cart pull</c>: manda o companheiro <b>puxar a carroça</b> livre mais próxima. Se já
        /// estiver preso a uma, só confirma.
        /// </summary>
        internal static bool Pull(CompanionAI companion, out string error)
        {
            error = null;

            if (!Enabled)
            {
                error = "[ModIA] A carroca esta desligada (Carroca/Ligado = false no .cfg).";
                return false;
            }

            if (companion == null)
            {
                error = "[ModIA] Nenhum companheiro no mundo.";
                return false;
            }

            Humanoid humanoid = companion.JobHumanoid;
            if (humanoid == null)
            {
                error = "[ModIA] O companheiro esta sem corpo/inventario.";
                return false;
            }

            if (Attached && _cart.IsAttached(humanoid))
            {
                error = "[ModIA] O companheiro ja esta puxando a carroca (modia_cart release para soltar).";
                return false;
            }

            Vagon cart = FindFreeCart(companion.transform.position, Radius);
            if (cart == null)
            {
                error = $"[ModIA] Nao achei carroca livre num raio de {Radius:0} m " +
                        "(e ela nao pode estar sendo puxada por voce ou por outro).";
                return false;
            }

            _cart = cart;
            _state = CartState.WalkToCart;
            _deadline = Time.time + AttachTimeout;

            Plugin.Log.LogInfo(
                $"[ModIA] Carroca: indo ate o engate de '{cart.name}' " +
                $"({Vector3.Distance(companion.transform.position, cart.transform.position):0.#} m).");
            CompanionJob.ShowHud("Companheiro indo puxar a carroca");
            return true;
        }

        /// <summary>
        /// <c>modia_cart release</c>: solta a carroça (o <c>Interact</c> do jogo desfaz o
        /// <c>ConfigurableJoint</c>). Devolve <c>true</c> quando havia algo para soltar.
        /// </summary>
        internal static bool Release(CompanionAI companion, string reason)
        {
            if (_state == CartState.None)
            {
                return false;
            }

            Humanoid humanoid = companion != null ? companion.JobHumanoid : null;
            bool wasAttached = Attached && humanoid != null && _cart.IsAttached(humanoid);

            if (wasAttached)
            {
                // O Vagon.Update (dono do ZDO) vê que já está presa e solta.
                _cart.Interact(humanoid, false, false);
                Plugin.Log.LogInfo($"[ModIA] Carroca: soltei a carroca ({reason}).");
            }

            _state = CartState.None;
            _cart = null;
            _attachGraceUntil = 0f;
            _attachConfirmed = false;
            return wasAttached;
        }

        // ---------------------------------------------------------------------
        // Ciclo (chamado pelo CompanionAI, só no dono do ZDO)
        // ---------------------------------------------------------------------

        /// <summary>
        /// Um passo da camada: andar até o engate e se prender (o jogo cria o <c>ConfigurableJoint</c>)
        /// ou vigiar a carroça que já está sendo puxada.
        /// </summary>
        internal static void Tick(CompanionAI companion, float dt)
        {
            if (companion == null || _state == CartState.None)
            {
                return;
            }

            if (!Enabled)
            {
                Release(companion, "carroca desligada na config");
                return;
            }

            Humanoid humanoid = companion.JobHumanoid;
            if (humanoid == null || _cart == null)
            {
                Cancel("o companheiro ou a carroca sumiu");
                return;
            }

            if (_state == CartState.Attached)
            {
                // O Vagon prende no Update dele (dono do ZDO): alguns ticks de tolerância antes de
                // concluir que não deu.
                if (_cart.IsAttached(humanoid))
                {
                    if (!_attachConfirmed)
                    {
                        _attachConfirmed = true;
                        _attachGraceUntil = 0f;

                        Plugin.Log.LogInfo(
                            $"[ModIA] Carroca: o companheiro esta puxando '{_cart.name}' e vai " +
                            "leva-la seguindo voce (modia_cart release para soltar).");
                        CompanionJob.ShowHud("Companheiro puxando a carroca");
                    }

                    return;
                }

                if (_attachGraceUntil > 0f && Time.time < _attachGraceUntil)
                {
                    return;
                }

                if (!_attachConfirmed)
                {
                    Plugin.Log.LogInfo(
                        "[ModIA] Carroca: a carroca nao aceitou o engate (talvez esteja em uso ou " +
                        "em posicao ruim); o companheiro voltou a seguir voce.");
                    Cancel("nao consegui prender a carroca");
                    return;
                }

                // A junta do jogo arrebenta se a carroça ficar presa demais: nesse caso ele deixa de
                // estar puxando e volta a te seguir (o follow nativo nunca parou).
                Plugin.Log.LogInfo(
                    "[ModIA] Carroca: a carroca se soltou (junta arrebentou ou ela ficou presa); " +
                    "o companheiro voltou a seguir voce.");
                Cancel("a carroca se soltou");
                return;
            }

            if (Time.time > _deadline)
            {
                Plugin.Log.LogInfo("[ModIA] Carroca: nao consegui chegar ao engate (tempo esgotado).");
                Cancel("nao consegui chegar ao engate");
                return;
            }

            Vector3 target = AttachPoint(_cart, companion.transform.position.y);
            float distance = Vector3.Distance(companion.transform.position, target);

            if (distance > AttachReach)
            {
                companion.JobApproach(dt, target, AttachReach, MovementOwner.Cart);
                return;
            }

            Attach(companion, humanoid);
        }

        /// <summary>
        /// Chegou no engate: encosta no ponto certo, vira para a carroça e chama o <c>Interact</c> do
        /// jogo. O <c>Vagon</c> (dono do ZDO) prende o corpo do companheiro no <c>Update</c> dele — o
        /// <c>m_playerExtraPullMass</c> do próprio jogo entra junto, como no jogador.
        /// </summary>
        private static void Attach(CompanionAI companion, Humanoid humanoid)
        {
            Vector3 target = AttachPoint(_cart, companion.transform.position.y);

            companion.JobTeleport(target, companion.transform.rotation, MovementOwner.Cart);

            Vector3 direction = _cart.transform.position - companion.transform.position;
            direction.y = 0f;
            if (direction.sqrMagnitude > 0.0001f)
            {
                companion.JobLookAt(companion.transform.position + direction.normalized);
            }

            // Se o jogador é o dono do ZDO da carroça, o Interact prende já neste tick; se não, o
            // Vagon pede a posse e prende alguns ticks depois — por isso o estado só fecha quando
            // IsAttached(companion) confirma (o Tick cuida disso).
            _cart.Interact(humanoid, false, false);

            _state = CartState.Attached;
            _attachConfirmed = false;
            _attachGraceUntil = Time.time + 3f;
        }

        /// <summary>
        /// Ponto do engate, na altura do companheiro (o <c>m_attachOffset</c> do <c>Vagon</c> é o
        /// ponto no corpo do puxador). É a mesma conta da referência:
        /// <c>m_attachPoint.position - m_attachOffset</c>, com o <c>y</c> do companheiro para ele não
        /// ser jogado para o alto.
        /// </summary>
        private static Vector3 AttachPoint(Vagon cart, float y)
        {
            Vector3 point = cart.m_attachPoint != null
                ? cart.m_attachPoint.position - cart.m_attachOffset
                : cart.transform.position;
            point.y = y;
            return point;
        }

        private static void Cancel(string reason)
        {
            Plugin.Log.LogInfo($"[ModIA] Carroca: pedido encerrado ({reason}).");
            _state = CartState.None;
            _cart = null;
            _attachGraceUntil = 0f;
            _attachConfirmed = false;
        }

        // ---------------------------------------------------------------------
        // Varredura
        // ---------------------------------------------------------------------

        /// <summary>
        /// Carroça <b>livre</b> mais próxima: nunca uma que já esteja presa (nem na sua mão, nem na de
        /// outro companheiro) nem em uso pelo contêiner dela (<c>Vagon.InUse</c>).
        /// </summary>
        private static Vagon FindFreeCart(Vector3 position, float radius)
        {
            int count = Physics.OverlapSphereNonAlloc(
                position, radius, s_colliderBuffer, VehicleMask, QueryTriggerInteraction.Collide);

            Vagon best = null;
            float bestDistance = radius;

            for (int i = 0; i < count; i++)
            {
                Collider collider = s_colliderBuffer[i];
                if (collider == null)
                {
                    continue;
                }

                Vagon cart = collider.GetComponentInParent<Vagon>();
                if (cart == null || cart.IsAttached() || cart.InUse())
                {
                    continue;
                }

                float distance = Vector3.Distance(cart.transform.position, position);
                if (distance < bestDistance)
                {
                    best = cart;
                    bestDistance = distance;
                }
            }

            return best;
        }

        private static int VehicleMask
        {
            get
            {
                if (s_vehicleMask == -1)
                {
                    s_vehicleMask = LayerMask.GetMask("vehicle", "Default", "piece", "piece_nonsolid");
                }

                return s_vehicleMask;
            }
        }

        // ---------------------------------------------------------------------
        // Leitura (console/menu)
        // ---------------------------------------------------------------------

        /// <summary>Linha de estado para o <c>modia_stats</c>/<c>modia_cart</c>.</summary>
        internal static string Describe()
        {
            switch (_state)
            {
                case CartState.WalkToCart:
                    return "indo puxar a carroca";

                case CartState.Attached:
                    return "puxando a carroca";

                default:
                    return Enabled
                        ? "nenhuma (modia_cart pull)"
                        : "desligada (Carroca/Ligado = false)";
            }
        }
    }
}

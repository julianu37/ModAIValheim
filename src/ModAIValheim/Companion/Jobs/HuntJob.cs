using System.Collections.Generic;
using UnityEngine;

namespace ModAIValheim.Companion.Jobs
{
    /// <summary>
    /// <b>TRABALHO: CAÇAR</b> (Fase 2.16a) — o companheiro vai atrás da fauna por perto
    /// (javali, cervo, lebre, neck, lox…), abate e <b>cata o que caiu</b>, e repete até a quantidade
    /// pedida ou até acabar a caça no raio.
    ///
    /// <para>
    /// <b>Como ele caça:</b> quem persegue e ataca é o <b>motor nativo</b> do jogo (o
    /// <c>MonsterAI</c> por baixo do <see cref="CompanionAI"/>): a mira, o pathfinding
    /// (<c>MoveTo</c>), a animação, a hitbox e o dano continuam sendo do jogo — o trabalho só
    /// <b>escolhe o alvo</b> e o entrega ao companheiro
    /// (<see cref="CompanionAI.JobEngage"/>), exatamente como o modo Aggressive faz com criaturas
    /// hostis. Assim o caçar <b>funciona com o combate existente</b> (mesma arma, mesmo bloqueio,
    /// mesma stamina), sem reimplementar ataque.
    /// </para>
    ///
    /// <para>
    /// <b>Quem pode ser caçado:</b> a fauna neutra (fação <c>AnimalsVeg</c>) que <b>deixa algo
    /// cair</b> — o catálogo (<see cref="HuntCatalog"/>) é lido dos próprios prefabs do jogo
    /// (<c>Character</c> + <c>CharacterDrop</c>), então criaturas de outros mods entram sozinhas.
    /// Nunca: jogadores, o próprio companheiro, criaturas <b>domadas</b> (boi, lobo domesticado…),
    /// chefes e criaturas que o jogo mandou ignorar (<c>m_aiSkipTarget</c>).
    /// </para>
    ///
    /// <para>
    /// <b>Como ele cata:</b> o abate entra na fila do <c>CompanionLoot</c> sozinho (o patch em
    /// <c>Character.OnDeath</c> vê que o último golpe foi do companheiro) — o trabalho só
    /// <b>espera</b> o loot terminar antes de sair caçando de novo (é o "abater, catar e voltar a
    /// repetir"), sem duplicar a lógica de catar.
    /// </para>
    ///
    /// <para>
    /// Adaptado (lógica) de <c>src/Companions/Jobs/HuntController.cs</c> do <i>Offline Companions
    /// Reborn</i> (MIT — ver <c>THIRD_PARTY_NOTICES.md</c>): a referência tem os mesmos estados
    /// (idle/caçando/voltando), o mesmo catálogo lido dos prefabs (<c>HuntCatalog.Ensure</c>), a
    /// mesma "troca por um alvo mais perto" e a mesma regra de largar a presa que se afastou da
    /// coleira. A implementação aqui é própria (adaptada ao modelo de camadas do
    /// <see cref="CompanionAI"/>, ao <c>CompanionLoot</c> da Fase 2.12 e à fila única de trabalho).
    /// </para>
    ///
    /// <para>
    /// <b>Fora do núcleo (2.16b):</b> caçar <b>pássaros</b> (<c>RandomFlyingBird</c>) — eles não são
    /// <c>Character</c>, então exigem um estado próprio de aproximação/arco (o "Birding" da
    /// referência, com arco + flechas e tiro manual), e por isso ficaram de fora desta entrega.
    /// </para>
    /// </summary>
    internal sealed class HuntJob : CompanionJob
    {
        private enum HuntState
        {
            /// <summary>Procurando presa (varredura com intervalo).</summary>
            Scanning,

            /// <summary>Perseguindo/atacando a presa (o motor nativo é quem age).</summary>
            Chasing,

            /// <summary>Esperando o <c>CompanionLoot</c> catar o abate antes de caçar de novo.</summary>
            Looting,
        }

        /// <summary>Distância (m) mínima para trocar a presa por uma mais perto (como na referência).</summary>
        private const float SwitchCloserBy = 5f;

        /// <summary>Tempo (s) que uma presa largada fica fora da conta (evita reciclar a mesma).</summary>
        private const float SkipSeconds = 60f;

        /// <summary>Varreduras vazias seguidas antes de considerar a caça encerrada.</summary>
        private const int EmptyScansBeforeStop = 3;

        /// <summary>Tempo (s) máximo esperando o loot antes de voltar a caçar.</summary>
        private const float LootWaitSeconds = 90f;

        private HuntState _state = HuntState.Scanning;
        private float _scanTimer;
        private float _lootWaitTimer;

        /// <summary>Presa atual (engajada no combate).</summary>
        private Character _prey;

        private string _preyName = string.Empty;

        private int _emptyScans;

        /// <summary>Presa largada (id → momento em que volta a valer).</summary>
        private readonly Dictionary<int, float> _skip = new Dictionary<int, float>();

        /// <summary>Buffer reaproveitado das varreduras (nenhuma lista alocada em jogo).</summary>
        private readonly List<Character> _buffer = new List<Character>();

        internal override JobKind Kind => JobKind.Hunt;

        internal override string DisplayName => "Caçar";

        internal override string CommandName => "hunt";

        protected override bool OnStart(CompanionAI companion, out string error)
        {
            error = null;

            // Sem modo de combate ele não ataca (e o Passive nunca revida): avisar é melhor do que
            // mandar o companheiro passear atrás dos javalis sem fazer nada.
            if (CompanionCombat.Mode == CompanionMode.Passive)
            {
                error =
                    "[ModIA] O companheiro esta em modo PASSIVO: ele nao ataca nada. " +
                    "Use modia_mode defensive (ou aggressive) antes de cacar.";
                return false;
            }

            if (companion.JobHumanoid == null)
            {
                error = "[ModIA] O companheiro esta sem corpo/inventario; cacar cancelado.";
                return false;
            }

            HuntCatalog.Ensure();

            _state = HuntState.Scanning;
            _scanTimer = 0f;
            _lootWaitTimer = 0f;
            _prey = null;
            _preyName = string.Empty;
            _emptyScans = 0;
            _skip.Clear();

            SetStatus("procurando caça");
            return true;
        }

        protected override void OnTick(CompanionAI companion, float dt)
        {
            // Um trabalho não caça em modo passivo: se o jogador trocou o modo no meio, encerra.
            if (CompanionCombat.Mode == CompanionMode.Passive)
            {
                Finish(companion, "modo passivo");
                return;
            }

            switch (_state)
            {
                case HuntState.Chasing:
                    TickChasing(companion);
                    break;

                case HuntState.Looting:
                    TickLooting(companion, dt);
                    break;

                default:
                    TickScanning(companion, dt);
                    break;
            }
        }

        protected override void OnStop(CompanionAI companion, string reason)
        {
            if (companion != null)
            {
                companion.JobDisengage($"caçar encerrado ({reason})");
            }

            _prey = null;
            _preyName = string.Empty;
            _skip.Clear();
            SetStatus("encerrado");
        }

        // ---------------------------------------------------------------------
        // Procurando presa
        // ---------------------------------------------------------------------

        private void TickScanning(CompanionAI companion, float dt)
        {
            // O loot manda antes do trabalho (prioridade combate > coleta > depósito > loot > trabalho):
            // o companheiro cata o abate anterior e só depois volta a caçar.
            if (CompanionLoot.HasWork)
            {
                _state = HuntState.Looting;
                _lootWaitTimer = 0f;
                SetStatus("catando o abate");
                return;
            }

            // Um inimigo de verdade no comando (revide do Defensive, hostil do Aggressive): o combate
            // vence o trabalho e a caça espera — sem engajar por cima de quem já está sendo lutado.
            if (companion.JobHasCombatTarget)
            {
                SetStatus("combate");
                return;
            }

            _scanTimer -= dt;
            if (_scanTimer > 0f)
            {
                SetStatus("procurando caça");
                return;
            }

            _scanTimer = CompanionJobsConfig.ScanInterval;

            Character prey = FindPrey(companion, out string why);
            if (prey == null)
            {
                _emptyScans++;
                LogEvent(
                    "hunt|empty|" + why,
                    $"[ModIA] Caçar: nada para caçar ({why}); varredura {_emptyScans}/{EmptyScansBeforeStop}.");

                if (_emptyScans >= EmptyScansBeforeStop)
                {
                    Finish(companion, AmountReached ? "meta atingida" : $"acabou a caça: {why}");
                    return;
                }

                SetStatus("procurando caça");
                return;
            }

            _emptyScans = 0;
            _prey = prey;
            _preyName = prey.m_name;

            // Engaja o alvo no companheiro: daqui para a frente quem persegue e ataca é o motor
            // nativo do jogo (o mesmo caminho do modo Aggressive) — com a mesma arma, bloqueio e
            // stamina do companheiro.
            companion.JobEngage(prey);
            _state = HuntState.Chasing;
            SetStatus($"caçando {_preyName}");

            LogEvent(
                "hunt|prey|" + prey.GetInstanceID(),
                $"[ModIA] Caçar: indo atrás de '{_preyName}' a " +
                $"{Vector3.Distance(prey.transform.position, companion.JobPosition):0.#} m.");
        }

        /// <summary>
        /// Escolhe a presa: a fauna mais próxima dentro do raio, que deixe algo cair e que continue
        /// dentro da "coleira" do jogador (a mesma distância que o <c>MonsterAI</c> usa para largar
        /// um alvo de criatura domada — escolher mais longe só faria o alvo ser largado no tick
        /// seguinte, como já aconteceu na coleta na Fase 2.11).
        /// </summary>
        private Character FindPrey(CompanionAI companion, out string why)
        {
            why = "nada no raio";
            float radius = CompanionJobsConfig.HuntRadius;
            Vector3 me = companion.JobPosition;

            GameObject follow = companion.FollowTarget;
            Vector3 anchor = follow != null ? follow.transform.position : me;
            float leash = CompanionCombat.LeashDistance;

            _buffer.Clear();
            Character.GetCharactersInRange(me, radius, _buffer);

            Character best = null;
            float bestDistance = float.MaxValue;
            int seen = 0;
            int leashRejected = 0;

            for (int i = 0; i < _buffer.Count; i++)
            {
                Character candidate = _buffer[i];
                if (!IsPrey(companion, candidate))
                {
                    continue;
                }

                float distance = Vector3.Distance(candidate.transform.position, me);
                if (distance > radius)
                {
                    continue;
                }

                if (follow != null &&
                    Vector3.Distance(candidate.transform.position, anchor) > leash)
                {
                    leashRejected++;
                    continue;
                }

                seen++;
                if (best != null && distance >= bestDistance)
                {
                    continue;
                }

                best = candidate;
                bestDistance = distance;
            }

            if (best == null)
            {
                why = leashRejected > 0
                    ? $"{leashRejected} presa(s) fora da coleira do jogador ({leash:0} m)"
                    : $"nenhuma fauna a menos de {radius:0} m";
            }

            return best;
        }

        /// <summary>
        /// A criatura pode ser caçada? Fauna neutra (<c>AnimalsVeg</c>) que deixa algo cair, viva, e
        /// que não seja o companheiro, um jogador, um domado, um chefe ou alguém vetado.
        /// </summary>
        private bool IsPrey(CompanionAI companion, Character candidate)
        {
            if (candidate == null || candidate == companion.JobCharacter || candidate.IsDead())
            {
                return false;
            }

            if (candidate.IsPlayer() || candidate.IsTamed() || candidate.IsBoss())
            {
                return false;
            }

            if (candidate.m_aiSkipTarget)
            {
                return false;
            }

            if (candidate.GetFaction() != Character.Faction.AnimalsVeg)
            {
                return false;
            }

            if (IsSkipped(candidate))
            {
                return false;
            }

            return HuntCatalog.IsPrey(candidate);
        }

        private bool IsSkipped(Character candidate)
        {
            if (_skip.Count == 0)
            {
                return false;
            }

            int id = candidate.GetInstanceID();
            if (!_skip.TryGetValue(id, out float until))
            {
                return false;
            }

            if (Time.time >= until)
            {
                _skip.Remove(id);
                return false;
            }

            return true;
        }

        private void Skip(Character candidate, string reason)
        {
            if (candidate == null)
            {
                return;
            }

            if (_skip.Count > 50)
            {
                _skip.Clear();
            }

            _skip[candidate.GetInstanceID()] = Time.time + SkipSeconds;
            LogEvent(
                "hunt|skip|" + candidate.GetInstanceID(),
                $"[ModIA] Caçar: deixando '{candidate.m_name}' de lado por {SkipSeconds:0} s ({reason}).");
        }

        // ---------------------------------------------------------------------
        // Perseguindo (quem age é o motor nativo)
        // ---------------------------------------------------------------------

        private void TickChasing(CompanionAI companion)
        {
            Character prey = _prey;

            // Presa destruída (virou ragdoll/foi removida): nada a contar, volta a varrer.
            if (prey == null)
            {
                companion.JobDisengage("a presa sumiu");
                _prey = null;
                _state = HuntState.Scanning;
                _scanTimer = CompanionJobsConfig.ScanInterval;
                SetStatus("procurando caça");
                return;
            }

            // Abate! O patch do CompanionLoot já enfileirou o corpo (foi o último golpe dele); o
            // trabalho conta o abate e espera o loot antes de caçar de novo.
            if (prey.IsDead() || prey.GetHealth() <= 0f)
            {
                AddProgress();
                Plugin.Log.LogInfo(
                    $"[ModIA] Caçar: abati '{_preyName}' " +
                    $"(abate {Progress}{(Amount > 0 ? $"/{Amount}" : "")}).");
                ShowHud($"Abate: {_preyName} ({Progress}{(Amount > 0 ? $"/{Amount}" : "")})");

                // Larga o alvo morto: o combate volta a poder escolher (um inimigo que apareça
                // durante o loot é lutado) e o corpo é assunto do CompanionLoot.
                companion.JobDisengage("abate concluido");

                _prey = null;
                _preyName = string.Empty;

                if (AmountReached)
                {
                    Finish(companion, "meta atingida");
                    return;
                }

                _state = HuntState.Looting;
                _lootWaitTimer = 0f;
                SetStatus("catando o abate");
                return;
            }

            Character target = companion.JobTarget;

            if (target != prey)
            {
                if (target == null)
                {
                    // Perdemos o alvo (a coleira do MonsterAI largou, 30 s sem sentir, o jogador se
                    // afastou…): veta por um tempo, solta o alvo do trabalho (o combate por modo volta
                    // a poder escolher) e varre de novo.
                    Skip(prey, "perdi o alvo");
                    companion.JobDisengage("perdi o alvo");
                    _prey = null;
                    _state = HuntState.Scanning;
                    _scanTimer = CompanionJobsConfig.ScanInterval;
                    SetStatus("procurando caça");
                    return;
                }

                // Outro alvo assumiu (um inimigo de verdade nos agrediu): o combate vence o
                // trabalho; a caça retoma quando ele acabar.
                SetStatus($"combate: {target.m_name}");
                return;
            }

            // Uma presa bem mais perto apareceu antes do primeiro golpe: troca (como o jogador
            // faria). Só vale enquanto ele não atacou ainda — depois disso a luta é a atual.
            if (!companion.JobInAttack && Time.time >= _nextSwitchCheck)
            {
                _nextSwitchCheck = Time.time + 2f;
                Character closer = FindPrey(companion, out _);
                if (closer != null && closer != prey)
                {
                    float current = Vector3.Distance(prey.transform.position, companion.JobPosition);
                    float candidate = Vector3.Distance(closer.transform.position, companion.JobPosition);
                    if (candidate < current - SwitchCloserBy)
                    {
                        _prey = closer;
                        _preyName = closer.m_name;
                        companion.JobEngage(closer);
                        LogEvent(
                            "hunt|switch|" + closer.GetInstanceID(),
                            $"[ModIA] Caçar: troquei para '{_preyName}' ({candidate:0.#} m) " +
                            $"em vez de '{prey.m_name}' ({current:0.#} m).");
                        SetStatus($"caçando {_preyName}");
                    }
                }
            }
            else
            {
                SetStatus($"caçando {_preyName}");
            }
        }

        private float _nextSwitchCheck;

        // ---------------------------------------------------------------------
        // Esperando o loot
        // ---------------------------------------------------------------------

        private void TickLooting(CompanionAI companion, float dt)
        {
            SetStatus("catando o abate");

            // Combate no comando (um inimigo apareceu enquanto ele ia ao corpo): o loot espera e o
            // relógio deste trabalho NÃO corre — é a prioridade combate > loot > trabalho.
            if (companion.JobHasCombatTarget)
            {
                return;
            }

            if (!CompanionLoot.HasWork)
            {
                _state = HuntState.Scanning;
                _scanTimer = 0f;
                _emptyScans = 0;
                return;
            }

            // O companheiro está indo atrás do que caiu: ele pode ter reengajado um inimigo no
            // caminho (o loot pausa e volta para a frente da fila). Aqui só esperamos, com teto —
            // um loot que não termina não pode prender o trabalho para sempre.
            _lootWaitTimer += dt;
            if (_lootWaitTimer >= LootWaitSeconds)
            {
                LogEvent(
                    "hunt|loot|timeout",
                    $"[ModIA] Caçar: o loot passou de {LootWaitSeconds:0} s; voltando a caçar " +
                    "(os itens continuam no chão e ele cata depois).");
                _state = HuntState.Scanning;
                _scanTimer = CompanionJobsConfig.ScanInterval;
            }
        }
    }

    /// <summary>
    /// Catálogo de <b>presas</b> do caçar (Fase 2.16a): quais prefabs de criatura deixam algo cair.
    ///
    /// <para>
    /// Lido <b>uma vez</b> dos próprios prefabs do jogo (<c>ZNetScene.m_prefabs</c>): todo
    /// <c>Character</c> com <c>CharacterDrop</c> e com pelo menos um drop, que não seja jogador, não
    /// seja chefe e não seja um companheiro nosso. Criaturas de outros mods entram sozinhas (é a
    /// mesma ideia do <c>HuntCatalog.Ensure</c> da referência). A varredura é caríssima para rodar
    /// por frame, então fica em cache pela vida do processo.
    /// </para>
    /// </summary>
    internal static class HuntCatalog
    {
        private static readonly HashSet<string> s_prey = new HashSet<string>();

        private static bool s_built;

        /// <summary>Monta o catálogo (idempotente; só quando o mundo já tem os prefabs em memória).</summary>
        internal static void Ensure()
        {
            if (s_built)
            {
                return;
            }

            ZNetScene scene = ZNetScene.instance;
            if (scene == null || scene.m_prefabs == null || scene.m_prefabs.Count == 0)
            {
                return; // ainda não dá: tenta de novo na próxima varredura do trabalho
            }

            s_built = true;
            int counted = 0;

            try
            {
                for (int i = 0; i < scene.m_prefabs.Count; i++)
                {
                    GameObject prefab = scene.m_prefabs[i];
                    if (prefab == null)
                    {
                        continue;
                    }

                    Character character = prefab.GetComponent<Character>();
                    if (character == null || character.IsPlayer() || character.m_boss)
                    {
                        continue;
                    }

                    if (prefab.GetComponent<CompanionAI>() != null)
                    {
                        continue; // nunca caçar um companheiro do mod
                    }

                    CharacterDrop drop = prefab.GetComponent<CharacterDrop>();
                    if (drop == null || drop.m_drops == null || drop.m_drops.Count == 0)
                    {
                        continue;
                    }

                    bool hasDrop = false;
                    for (int d = 0; d < drop.m_drops.Count; d++)
                    {
                        CharacterDrop.Drop entry = drop.m_drops[d];
                        if (entry != null && entry.m_prefab != null && entry.m_amountMax > 0)
                        {
                            hasDrop = true;
                            break;
                        }
                    }

                    if (!hasDrop)
                    {
                        continue;
                    }

                    if (s_prey.Add(prefab.name))
                    {
                        counted++;
                    }
                }

                Plugin.Log.LogInfo(
                    $"[ModIA] Caçar: catalogo com {counted} tipo(s) de presa (criaturas que deixam " +
                    "algo cair).");
            }
            catch (System.Exception e)
            {
                // Ler os prefabs é opcional: um erro aqui não pode impedir o trabalho de rodar.
                Plugin.Log.LogWarning($"[ModIA] Caçar: falha ao montar o catalogo de presas: {e.Message}");
            }
        }

        /// <summary>Nome do prefab da criatura (pelo ZDO; o nome do GameObject pode ter "(Clone)").</summary>
        internal static string PrefabName(Character character)
        {
            ZNetView netView = character.GetComponent<ZNetView>();
            ZDO zdo = netView != null && netView.IsValid() ? netView.GetZDO() : null;
            GameObject prefab = zdo != null && ZNetScene.instance != null
                ? ZNetScene.instance.GetPrefab(zdo.GetPrefab())
                : null;

            if (prefab != null)
            {
                return prefab.name;
            }

            return Utils.GetPrefabName(character.gameObject.name);
        }

        /// <summary>Esta criatura está no catálogo de presas?</summary>
        internal static bool IsPrey(Character character)
        {
            if (character == null || s_prey.Count == 0)
            {
                return false;
            }

            return s_prey.Contains(PrefabName(character));
        }
    }
}

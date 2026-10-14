using System;
using System.Globalization;
using BepInEx.Configuration;
using ModAIValheim.Companion.Jobs;
using UnityEngine;

namespace ModAIValheim.Companion
{
    /// <summary>
    /// <b>Zona da casa</b> (Fase 2.17): o pedaço de mundo que o jogador chama de "base" — um
    /// <b>centro + raio</b>, como a zona de fazenda (<c>Companion/Jobs/FarmZone.cs</c>).
    ///
    /// <para>
    /// O jogador define com <c>modia_home set [raio]</c> no ponto onde está (a config
    /// <c>Casa/HomeZona</c> guarda o resultado entre sessões) e limpa com <c>modia_home clear</c>.
    /// A casa é o ponto de referência do companheiro para <b>depositar</b> (o baú de dentro da
    /// casa ganha preferência), para o <b>cuidado da base</b> (o trabalho de manutenção trabalha
    /// dentro desse raio) e para <b>voltar quando ficar perdido</b>
    /// (<c>Casa/VoltarSePerdido</c>).
    /// </para>
    ///
    /// <para>
    /// Adaptado (lógica) de <c>src/Companions/Jobs/HomeZoneVisual.cs</c> e
    /// <c>src/Companions/UI/HomeZonePanel.cs</c> do <i>Offline Companions Reborn</i> (MIT — ver
    /// <c>THIRD_PARTY_NOTICES.md</c>): lá a casa é uma <b>posição</b> guardada no ZDO do companheiro
    /// (<c>SetHomePosition</c>/<c>GetHomePosition</c>, com <c>HomeTripRange</c>/<c>AtHomeRange</c>)
    /// e desenhada por um <c>LineRenderer</c> enquanto o jogador ajusta; aqui ela é um <b>círculo</b>
    /// (centro + raio) definido por comando e serializado como texto simples na config — o visual da
    /// zona e o painel de ajuste ficaram <b>fora</b> desta entrega (ver o CHANGELOG).
    /// </para>
    /// </summary>
    internal struct HomeZone
    {
        /// <summary>Centro da casa (o ponto onde o jogador definiu).</summary>
        internal Vector3 Center;

        /// <summary>Raio (m) da casa.</summary>
        internal float Radius;

        /// <summary>True quando a casa tem um raio válido (senão não há casa definida).</summary>
        internal bool IsValid => Radius > 0f;

        /// <summary>O ponto está dentro da casa (no plano XZ)?</summary>
        internal bool Contains(Vector3 pos)
        {
            float dx = pos.x - Center.x;
            float dz = pos.z - Center.z;
            return dx * dx + dz * dz <= Radius * Radius;
        }

        /// <summary>Distância no plano (XZ) do centro — o Y da casa não interessa.</summary>
        internal float FlatDistance(Vector3 pos)
        {
            float dx = pos.x - Center.x;
            float dz = pos.z - Center.z;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        /// <summary>
        /// Serializa a casa para a config (<c>x;y;z;raio</c>, invariante de cultura). O <c>;</c> é o
        /// separador pelo mesmo motivo da zona de fazenda: nenhum texto nosso o usa.
        /// </summary>
        internal string Serialize()
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "{0:0.##};{1:0.##};{2:0.##};{3:0.##}",
                Center.x,
                Center.y,
                Center.z,
                Radius);
        }

        /// <summary>Lê a casa da config. Devolve <c>false</c> quando o texto não é uma casa válida.</summary>
        internal static bool TryParse(string text, out HomeZone zone)
        {
            zone = default;
            if (string.IsNullOrEmpty(text))
            {
                return false;
            }

            string[] parts = text.Split(';');
            if (parts.Length < 4)
            {
                return false;
            }

            if (!float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float x) ||
                !float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float y) ||
                !float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float z) ||
                !float.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out float radius))
            {
                return false;
            }

            zone.Center = new Vector3(x, y, z);
            zone.Radius = Mathf.Max(0f, radius);
            return zone.IsValid;
        }

        /// <summary>Descrição curta para o console/HUD (<c>centro 12,4 / raio 20 m</c>).</summary>
        internal string Describe()
        {
            return IsValid
                ? $"{Center.x:0.#}, {Center.z:0.#} (raio {Radius:0.#} m)"
                : "nenhuma (use modia_home set)";
        }
    }

    /// <summary>
    /// <b>A casa do companheiro</b> (Fase 2.17): a zona definida pelo jogador e as três coisas que
    /// dependem dela —
    /// <list type="number">
    ///   <item><description>o <b>baú da casa</b> ganha preferência no depósito da Fase 2.14
    ///   (<see cref="ChestPreferenceBonus"/>);</description></item>
    ///   <item><description>o <b>trabalho de cuidado da base</b> (<c>Jobs/UpkeepJob.cs</c>) trabalha
    ///   dentro do raio e volta para o centro quando não tem o que fazer;</description></item>
    ///   <item><description><b>voltar para casa</b> quando o companheiro ficar perdido
    ///   (<c>Casa/VoltarSePerdido</c>, desligado por padrão) — é o gancho que a Fase 2.18
    ///   (descansar/dormir na casa) vai reaproveitar.</description></item>
    /// </list>
    /// </summary>
    ///
    /// <para>
    /// <b>Persistência:</b> a casa vive na config (<c>Casa/HomeZona</c>), escrita pelo comando
    /// <c>modia_home set</c> com <c>ConfigFile.Save()</c> — é o mesmo caminho da zona de fazenda
    /// (<c>Jobs/FarmZona</c>), então ela sobrevive a reinícios sem um arquivo de estado próprio. O
    /// snapshot do companheiro (<see cref="CompanionStateStore"/>) guarda o que é <i>dele</i>
    /// (itens, comida, perícias); a casa é do <i>mundo</i>, não do NPC.
    /// </para>
    ///
    /// <para>
    /// Adaptado (lógica) de <c>src/Companions/Companion/CompanionSetup.cs</c>
    /// (<c>SetHomePosition</c>/<c>GetHomePosition</c>/<c>HasHomePosition</c>) e de
    /// <c>src/Companions/AI/MHR_ToolKeeper.cs</c> (<c>NearHome</c>/<c>HomeRange</c>/<c>AtHomeRange</c>)
    /// do <i>Offline Companions Reborn</i> (MIT — ver <c>THIRD_PARTY_NOTICES.md</c>), sem cópia de
    /// arquivo.
    /// </para>
    internal static class CompanionHome
    {
        /// <summary>Distância (m) do centro em que consideramos que o companheiro "chegou em casa".</summary>
        private const float ArriveReach = 4f;

        private static ConfigEntry<string> _homeZone;
        private static ConfigEntry<float> _homeRadius;
        private static ConfigEntry<bool> _preferChestInHome;
        private static ConfigEntry<bool> _returnIfLost;
        private static ConfigEntry<float> _lostDistance;

        /// <summary>Texto da config de onde a zona em cache foi lida (evita re-parse por tick).</summary>
        private static string s_zoneRaw;
        private static HomeZone s_zoneParsed;

        /// <summary>True enquanto a camada de "voltar para casa" está dirigindo o companheiro.</summary>
        private static bool s_returning;

        /// <summary>Casa definida (config <c>Casa/HomeZona</c> com um raio válido).</summary>
        internal static bool IsSet => Zone.IsValid;

        /// <summary>
        /// A casa gravada na config ("" = nenhuma). O <c>modia_home set</c> é quem a escreve; o
        /// resto do mod só lê. O parse é cacheado pelo texto (a propriedade é lida por tick).
        /// </summary>
        internal static HomeZone Zone
        {
            get
            {
                string raw = _homeZone != null ? _homeZone.Value : null;
                if (!string.Equals(raw, s_zoneRaw, StringComparison.Ordinal))
                {
                    s_zoneRaw = raw;
                    s_zoneParsed = HomeZone.TryParse(raw, out HomeZone zone) ? zone : default;
                }

                return s_zoneParsed;
            }
        }

        /// <summary>Raio padrão da casa quando o <c>modia_home set</c> não diz um (<c>Casa/HomeRaio</c>).</summary>
        internal static float DefaultRadius =>
            _homeRadius != null ? Mathf.Clamp(_homeRadius.Value, 4f, 120f) : 20f;

        /// <summary>
        /// O depósito prefere um baú <b>dentro da casa</b> (config <c>Casa/PreferirBauDaCasa</c>).
        /// Ligado por padrão; sem casa definida não tem efeito nenhum.
        /// </summary>
        internal static bool PreferChestInHome =>
            _preferChestInHome == null || _preferChestInHome.Value;

        /// <summary>
        /// Bônus (m) que um baú dentro da casa leva na escolha do depósito: um baú da casa vence
        /// qualquer outro até o raio da casa de distância. É o "depositar em casa" da Fase 2.17.
        /// </summary>
        internal static float ChestPreferenceBonus
        {
            get
            {
                HomeZone zone = Zone;
                return zone.IsValid ? zone.Radius : 0f;
            }
        }

        /// <summary>Voltar para casa quando o companheiro ficar perdido (<c>Casa/VoltarSePerdido</c>).</summary>
        internal static bool ReturnIfLost => _returnIfLost != null && _returnIfLost.Value;

        /// <summary>Distância (m) do jogador a partir da qual ele é considerado "perdido" (<c>Casa/DistanciaPerdido</c>).</summary>
        internal static float LostDistance =>
            _lostDistance != null ? Mathf.Clamp(_lostDistance.Value, 20f, 500f) : 80f;

        internal static void Configure(
            ConfigEntry<string> homeZone,
            ConfigEntry<float> homeRadius,
            ConfigEntry<bool> preferChestInHome,
            ConfigEntry<bool> returnIfLost,
            ConfigEntry<float> lostDistance)
        {
            _homeZone = homeZone;
            _homeRadius = homeRadius;
            _preferChestInHome = preferChestInHome;
            _returnIfLost = returnIfLost;
            _lostDistance = lostDistance;

            Plugin.Log.LogInfo(
                $"[ModIA] Casa: {Zone.Describe()} | raio padrao do comando = {DefaultRadius:0} m | " +
                $"preferir bau da casa = {PreferChestInHome} | voltar se perdido = {ReturnIfLost}" +
                (ReturnIfLost ? $" (a mais de {LostDistance:0} m do jogador)" : string.Empty) + ".");
        }

        /// <summary>Grava a casa na config e salva o arquivo (comando <c>modia_home set</c>).</summary>
        internal static void SetHome(Vector3 center, float radius)
        {
            if (_homeZone == null)
            {
                return;
            }

            HomeZone zone = new HomeZone
            {
                Center = center,
                Radius = Mathf.Clamp(radius, 4f, 120f),
            };

            _homeZone.Value = zone.Serialize();
            _homeZone.ConfigFile.Save();
            s_zoneRaw = null; // força o re-parse no próximo acesso
        }

        /// <summary>Apaga a casa (comando <c>modia_home clear</c>).</summary>
        internal static void ClearHome()
        {
            if (_homeZone == null)
            {
                return;
            }

            _homeZone.Value = string.Empty;
            _homeZone.ConfigFile.Save();
            s_zoneRaw = null;
            s_returning = false;
        }

        /// <summary>Linha de estado para o <c>modia_stats</c>/console.</summary>
        internal static string Describe()
        {
            HomeZone zone = Zone;
            if (!zone.IsValid)
            {
                return "nenhuma (modia_home set)";
            }

            return zone.Describe() +
                   (PreferChestInHome ? ", bau da casa preferido no deposito" : string.Empty) +
                   (ReturnIfLost ? $", volta se perdido (> {LostDistance:0} m)" : string.Empty);
        }

        /// <summary>
        /// <b>Voltar para casa quando perdido</b> (Fase 2.17, config <c>Casa/VoltarSePerdido</c>).
        ///
        /// <para>
        /// Só age quando o companheiro está <b>longe dos dois</b> — da casa e do jogador seguido —
        /// e não há nada mais importante no comando (trabalho, coleta, depósito, combate). É o caso
        /// do companheiro que ficou para trás (jogador atravessou um portal, morreu longe, o NPC se
        /// prendeu): em vez de atravessar o mundo atrás do jogador, ele volta para a base e espera.
        /// </para>
        ///
        /// <para>
        /// Reivindica o <see cref="MovementOwner.Upkeep"/> quando dirige o passo, então a guarda de
        /// follow (<c>CompanionAI.UpdateFollowSafety</c>) não disputa o frame. Desligado por padrão:
        /// com a chave em <c>false</c> nada aqui roda.
        /// </para>
        /// </summary>
        internal static void TickReturn(CompanionAI companion, float dt)
        {
            if (!ReturnIfLost || companion == null)
            {
                s_returning = false;
                return;
            }

            HomeZone zone = Zone;
            if (!zone.IsValid)
            {
                s_returning = false;
                return;
            }

            Vector3 me = companion.transform.position;
            float toHome = zone.FlatDistance(me);

            // Já em casa (ou chegando): solta o comando e o follow nativo volta a mandar.
            if (toHome <= ArriveReach)
            {
                if (s_returning)
                {
                    s_returning = false;
                    Plugin.Log.LogInfo("[ModIA] Casa: o companheiro chegou em casa e espera por voce.");
                    CompanionJob.ShowHud("Companheiro de volta a base");
                }

                return;
            }

            // Perto do jogador? Então não está perdido: o follow cuida dele.
            GameObject target = companion.FollowTarget;
            if (target != null)
            {
                Vector3 playerPos = target.transform.position;
                float dx = playerPos.x - me.x;
                float dz = playerPos.z - me.z;
                float toPlayer = Mathf.Sqrt(dx * dx + dz * dz);
                if (toPlayer <= LostDistance)
                {
                    s_returning = false;
                    return;
                }
            }

            if (!s_returning)
            {
                s_returning = true;
                Plugin.Log.LogInfo(
                    $"[ModIA] Casa: o companheiro esta a {toHome:0} m da base e longe do jogador " +
                    $"(> {LostDistance:0} m); voltando para casa.");
                CompanionJob.ShowHud("Companheiro perdido: voltando para a base");
            }

            companion.JobApproach(dt, zone.Center, ArriveReach, MovementOwner.Upkeep);
        }

        /// <summary>Solta o estado estático (unload do plugin / fim do mundo).</summary>
        internal static void Shutdown()
        {
            s_returning = false;
        }
    }
}

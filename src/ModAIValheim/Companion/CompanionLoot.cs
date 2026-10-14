using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using UnityEngine;

namespace ModAIValheim.Companion
{
    /// <summary>
    /// Um abate feito <b>pelo companheiro</b> que ainda precisa ser catado (Fase 2.12 — item #4 da
    /// <c>docs/audit-vs-offline-companions.md</c>).
    ///
    /// <para>
    /// O que interessa guardar é o <b>ponto</b> onde a criatura morreu e o <b>conjunto de itens que
    /// já estavam no chão</b> naquele instante (<see cref="Before"/>): é isso que garante o contrato
    /// da feature — catamos <b>só</b> o que <i>este</i> abate gerou, nunca a tralha que já estava
    /// por ali.
    /// </para>
    /// </summary>
    internal sealed class LootKill
    {
        /// <summary>Onde o corpo caiu (o ragdoll escorrega — <see cref="CompanionLoot.TrackBody"/> atualiza).</summary>
        internal Vector3 Position;

        /// <summary>Nome (token) da criatura abatida. Só para o log.</summary>
        internal string VictimName;

        /// <summary>Momento do abate (<see cref="Time.time"/>).</summary>
        internal float KillTime;

        /// <summary>Momento em que o companheiro começou a ir catar este abate.</summary>
        internal float Start;

        /// <summary>Itens (<c>ItemDrop</c>) que já estavam no chão no instante do abate.</summary>
        internal readonly HashSet<int> Before = new HashSet<int>();

        /// <summary>Itens já considerados/pegos (permite seguir um drop que rolou para longe).</summary>
        internal readonly HashSet<int> Seen = new HashSet<int>();

        /// <summary>Itens descartados de vez (presos no piche, inalcançáveis).</summary>
        internal readonly HashSet<int> Skipped = new HashSet<int>();

        /// <summary>True enquanto existir um ragdoll deste abate (os itens só saem quando ele some).</summary>
        internal bool Body;

        /// <summary>True depois da primeira sondagem de corpo (antes disso <see cref="Body"/> não vale).</summary>
        internal bool BodyChecked;

        /// <summary>True se já vimos/pegamos pelo menos um item deste abate.</summary>
        internal bool SawDrop;

        /// <summary>True quando o último varredura achou item mas o inventário não tinha espaço.</summary>
        internal bool BlockedBySpace;

        /// <summary>Momento do último item visto/pegado.</summary>
        internal float LastDrop;

        /// <summary>Momento em que chegamos ao local (-1 = ainda não chegamos).</summary>
        internal float ArrivedAt = -1f;

        /// <summary>Momento do último progresso na aproximação do local (guarda anti-travamento).</summary>
        internal float LastProgressAt;

        /// <summary>Última distância medida ao local (-1 = nenhuma).</summary>
        internal float LastDistance = -1f;

        /// <summary>Já chegamos ao ponto do abate?</summary>
        internal bool Arrived => ArrivedAt >= 0f;

        /// <summary>Marca a chegada ao ponto do abate (só na primeira vez).</summary>
        internal void MarkArrived()
        {
            if (ArrivedAt < 0f)
            {
                ArrivedAt = Time.time;
            }
        }
    }

    /// <summary>
    /// <b>Catar os drops de criatura que o companheiro mesmo abateu</b> (Fase 2.12, item #4 da
    /// auditoria comparativa vs <i>Offline Companions Reborn</i>, MIT — ver
    /// <c>THIRD_PARTY_NOTICES.md</c>).
    ///
    /// <para>
    /// <b>Problema:</b> o companheiro mata um cervo/javali e simplesmente segue em frente — o couro,
    /// as penas e os ossos ficam no chão para sempre.
    /// </para>
    ///
    /// <para>
    /// <b>Solução:</b> um patch Harmony <b>observador</b> em <c>Character.OnDeath</c>
    /// (<see cref="CompanionLootPatches"/>) detecta quando quem morreu levou o <b>último golpe do
    /// companheiro</b> e enfileira o <b>ponto do abate</b>. Depois — <b>fora do combate e fora de uma
    /// tarefa de coleta</b> — o companheiro vai até o corpo, <b>espera os itens saírem</b> (eles são
    /// gerados pelo ragdoll só quando ele desaparece, ver <c>Ragdoll.DestroyNow → SpawnLoot</c>) e
    /// cata um por um, respeitando raio, distância máxima, peso e o limite da fila.
    /// </para>
    ///
    /// <para>
    /// <b>Como sabemos que o abate foi dele:</b> no Valheim 1.0.17 o <c>Character.Damage(HitData)</c>
    /// grava o golpe em <c>Character.m_lastHit</c> (o golpe que deixou a criatura viva pela última
    /// vez) e o <c>Character.OnDeath</c> já lê esse mesmo campo para creditar o abate ao jogador
    /// (<c>m_lastHit?.GetAttacker()</c>). O <c>Attack.DoAttack</c> preenche o
    /// <c>HitData.m_attacker</c> com o ZDOID de quem golpeou (<c>hitData.SetAttacker(m_character)</c>),
    /// então <c>GetAttacker()</c> devolve o <see cref="Character"/> do companheiro — é só checar se
    /// ele tem um <see cref="CompanionAI"/>. Nenhum estado novo precisa ser mantido por golpe.
    /// </para>
    ///
    /// <para>
    /// <b>Este tipo guarda estado e política</b> (fila, filtros, prazos, peso, sondagens). O
    /// comportamento por frame (andar/mirar/pegar) fica no <c>CompanionAI.UpdateLoot</c>, que é quem
    /// roda no dono do ZDO — o mesmo modelo do resto do mod.
    /// </para>
    /// </summary>
    internal static class CompanionLoot
    {
        /// <summary>Tamanho do buffer das sondagens (reaproveitado; nada alocado por chamada).</summary>
        private const int ColliderBufferSize = 128;

        /// <summary>Abates mais próximos que isso são o mesmo evento (evita duplicar a fila).</summary>
        private const float MinKillSeparation = 1f;

        /// <summary>Intervalo (s) da sondagem de corpo (ragdoll) — nunca por frame.</summary>
        private const float BodyCheckInterval = 1f;

        /// <summary>Folga (m) do raio ao procurar o ragdoll deste abate.</summary>
        private const float BodyScanSlack = 3f;

        /// <summary>Tempo (s) sem achar nada novo depois de já ter visto drops para encerrar.</summary>
        private const float SeenDropSettleSeconds = 1.5f;

        /// <summary>Tempo (s) parado no local, sem corpo e sem drops, para desistir.</summary>
        private const float NoDropGraceSeconds = 2f;

        /// <summary>Idade mínima (s) do abate antes de aceitar "não caiu nada".</summary>
        private const float NoDropMinAgeSeconds = 8f;

        /// <summary>Segundos sem reduzir a distância ao local antes de desistir de chegar lá.</summary>
        private const float NoProgressSeconds = 6f;

        /// <summary>Intervalo (s) entre varreduras de drops (nunca por frame).</summary>
        internal const float ScanInterval = 0.4f;

        /// <summary>Distância (m) em que consideramos ter "chegado" ao ponto do abate.</summary>
        internal const float ArriveDistance = 4f;

        /// <summary>Distância (m) em que paramos ao ir até o ponto do abate.</summary>
        internal const float ArriveStop = 2f;

        /// <summary>Distância (m, no plano) em que o item já está ao alcance da mão.</summary>
        internal const float PickupReach = 1.6f;

        /// <summary>Desnível (m) tolerado ao pegar um item.</summary>
        internal const float PickupVertical = 2f;

        // ---------------------------------------------------------------------
        // Configuração
        // ---------------------------------------------------------------------

        private static ConfigEntry<bool> _enabled;
        private static ConfigEntry<float> _radius;
        private static ConfigEntry<float> _maxDistance;
        private static ConfigEntry<int> _queueLimit;
        private static ConfigEntry<float> _waitSeconds;
        private static ConfigEntry<float> _maxSeconds;
        private static ConfigEntry<float> _maxWeight;

        /// <summary>Ligar/desligar a feature (config <c>Loot/CatarDropsDeCriatura</c>).</summary>
        internal static bool Enabled => _enabled == null || _enabled.Value;

        /// <summary>Raio (m) da varredura de itens em volta do ponto do abate.</summary>
        internal static float Radius => _radius != null ? Mathf.Clamp(_radius.Value, 2f, 30f) : 8f;

        /// <summary>Distância máxima (m) do abate ao companheiro e ao jogador seguido.</summary>
        internal static float MaxDistance => _maxDistance != null ? Mathf.Clamp(_maxDistance.Value, 5f, 200f) : 30f;

        /// <summary>Quantos abates ficam na fila ao mesmo tempo.</summary>
        internal static int QueueLimit => _queueLimit != null ? Mathf.Clamp(_queueLimit.Value, 1, 20) : 6;

        /// <summary>Tempo (s) máximo parado no local esperando os itens saírem do corpo.</summary>
        internal static float WaitSeconds => _waitSeconds != null ? Mathf.Clamp(_waitSeconds.Value, 3f, 60f) : 15f;

        /// <summary>Teto (s) do trabalho de um abate (nunca deixa o companheiro vagando).</summary>
        internal static float MaxSeconds => _maxSeconds != null ? Mathf.Clamp(_maxSeconds.Value, 5f, 180f) : 45f;

        /// <summary>
        /// Peso máximo (kg) que o companheiro aceita carregar de loot. <c>0</c> (padrão) = sem limite:
        /// o jogo não tem limite de peso para personagens, então o teto é opcional.
        /// </summary>
        internal static float MaxWeight => _maxWeight != null ? Mathf.Max(0f, _maxWeight.Value) : 0f;

        internal static void Configure(
            ConfigEntry<bool> enabled,
            ConfigEntry<float> radius,
            ConfigEntry<float> maxDistance,
            ConfigEntry<int> queueLimit,
            ConfigEntry<float> waitSeconds,
            ConfigEntry<float> maxSeconds,
            ConfigEntry<float> maxWeight)
        {
            _enabled = enabled;
            _radius = radius;
            _maxDistance = maxDistance;
            _queueLimit = queueLimit;
            _waitSeconds = waitSeconds;
            _maxSeconds = maxSeconds;
            _maxWeight = maxWeight;

            Plugin.Log.LogInfo(
                $"[ModIA] Loot: catar drops de criatura = {Enabled} | raio = {Radius} m | " +
                $"distância máxima = {MaxDistance} m | fila = {QueueLimit} | " +
                $"espera = {WaitSeconds} s (teto {MaxSeconds} s) | " +
                $"peso máximo = {(MaxWeight > 0f ? MaxWeight + " kg" : "sem limite")}.");
        }

        // ---------------------------------------------------------------------
        // Estado (o companheiro é único, como no resto do mod)
        // ---------------------------------------------------------------------

        private static readonly List<LootKill> s_queue = new List<LootKill>();
        private static LootKill s_current;
        private static int s_ownerId;

        /// <summary>Buffer reaproveitado das sondagens de itens/corpo.</summary>
        private static readonly Collider[] s_buffer = new Collider[ColliderBufferSize];

        private static int s_itemMask = -1;
        private static float s_bodyCheckAt;

        /// <summary>Existe abate para catar (na mão ou na fila)?</summary>
        internal static bool HasWork => s_current != null || s_queue.Count > 0;

        /// <summary>Abate que o companheiro está catando agora (null = nenhum).</summary>
        internal static LootKill Current => s_current;

        /// <summary>Solta todo o estado (unload do plugin, dismiss, morte do companheiro).</summary>
        internal static void Shutdown()
        {
            s_queue.Clear();
            s_current = null;
            s_ownerId = 0;
            s_bodyCheckAt = 0f;
        }

        /// <summary>
        /// Registra um abate do companheiro (chamado pelo patch de <c>Character.OnDeath</c>).
        /// Barato por construção: só roda quando <b>alguém morre</b> e, na esmagadora maioria das
        /// vezes, sai na primeira comparação (o atacante não é o companheiro).
        /// </summary>
        internal static void RegisterKill(CompanionAI companion, Character victim)
        {
            if (!Enabled || companion == null || victim == null)
            {
                return;
            }

            // Só o dono do ZDO age (é ele que anda/pega; os demais clientes veriam o resultado).
            if (!companion.IsLocallyOwned)
            {
                return;
            }

            if (victim.IsPlayer() || victim.GetComponent<CompanionAI>() != null)
            {
                return;
            }

            // Sem CharacterDrop não há o que catar (a criatura não dropa nada).
            if (victim.GetComponent<CharacterDrop>() == null)
            {
                return;
            }

            Vector3 position = victim.transform.position;
            int ownerId = companion.GetInstanceID();
            if (ownerId != s_ownerId)
            {
                // Companheiro novo (respawn): a fila do antigo não vale mais.
                s_queue.Clear();
                s_current = null;
                s_ownerId = ownerId;
            }

            if (IsKnownKill(position))
            {
                return;
            }

            LootKill kill = new LootKill
            {
                Position = position,
                VictimName = victim.m_name,
                KillTime = Time.time,
                LastDrop = Time.time,
            };

            // Fotografa o que JÁ estava no chão: é o que nos deixa catar só o deste abate.
            CollectItemIds(position, kill.Before);

            if (s_queue.Count >= QueueLimit)
            {
                // Fila cheia: descarta o mais antigo (é o mais provável de já ter sido perdido).
                LootKill dropped = s_queue[0];
                s_queue.RemoveAt(0);
                Plugin.Log.LogInfo(
                    $"[ModIA] Loot: fila cheia ({QueueLimit}); abandonei o abate de " +
                    $"'{Loc(dropped.VictimName)}'.");
            }

            s_queue.Add(kill);
            Plugin.Log.LogInfo(
                $"[ModIA] Loot: o companheiro abateu '{Loc(kill.VictimName)}' " +
                $"({kill.Before.Count} item(ns) já estavam no chão ali); " +
                $"{s_queue.Count + (s_current != null ? 1 : 0)} abate(s) para catar.");
        }

        /// <summary>Pega o próximo abate da fila (true se já existe um em andamento).</summary>
        internal static bool TryBeginNext()
        {
            if (s_current != null)
            {
                return true;
            }

            if (s_queue.Count == 0)
            {
                return false;
            }

            s_current = s_queue[0];
            s_queue.RemoveAt(0);
            s_current.Start = Time.time;
            s_current.ArrivedAt = -1f;
            s_current.SawDrop = false;
            s_current.Body = false;
            s_current.BodyChecked = false;
            s_current.LastProgressAt = Time.time;
            s_current.LastDistance = -1f;
            s_bodyCheckAt = 0f;

            CompanionAI companion = CompanionManager.ActiveCompanion;
            float distance = companion != null
                ? Vector3.Distance(companion.transform.position, s_current.Position)
                : 0f;

            LogOnce(
                "begin|" + s_current.VictimName + "|" + (int)s_current.Position.x + "|" + (int)s_current.Position.z,
                $"[ModIA] Loot: indo catar o que '{Loc(s_current.VictimName)}' deixou " +
                $"a {distance:0.#} m ({s_queue.Count} abate(s) ainda na fila).");
            return true;
        }

        /// <summary>
        /// Devolve o abate em andamento para a <b>frente da fila</b> (combate ou tarefa de coleta
        /// assumiu o companheiro). O relógio do abate não corre enquanto ele está parado — quando o
        /// caminho liberar, ele recomeça de onde parou, em vez de "expirar" parado.
        /// </summary>
        internal static void Pause(string reason)
        {
            if (s_current == null)
            {
                return;
            }

            LootKill paused = s_current;
            s_current = null;
            s_queue.Insert(0, paused);

            LogOnce(
                "pause|" + paused.VictimName,
                $"[ModIA] Loot: '{Loc(paused.VictimName)}' de volta à fila ({reason}); " +
                "retomo quando o companheiro estiver livre.");
        }

        /// <summary>
        /// Encerra o abate atual (com motivo no log) e libera o próximo da fila. Sem abate em
        /// andamento não faz nada.
        /// </summary>
        internal static void Finish(string reason)
        {
            if (s_current == null)
            {
                return;
            }

            LootKill done = s_current;
            s_current = null;

            Plugin.Log.LogInfo(
                $"[ModIA] Loot: fim do abate de '{Loc(done.VictimName)}' ({reason}); " +
                $"{done.Seen.Count} item(ns) visto(s), {s_queue.Count} abate(s) na fila.");
        }

        // ---------------------------------------------------------------------
        // Sondagens (buffers reaproveitados, nada por frame)
        // ---------------------------------------------------------------------

        /// <summary>
        /// <c>ItemDrop</c> elegível deste abate mais próximo do companheiro — a varredura do estado
        /// de loot. Só devolve item que <b>este</b> abate gerou (fora do <see cref="LootKill.Before"/>),
        /// com auto-pickup ligado (o mesmo filtro que o jogador usa ao passar por cima), fora do piche
        /// e que caiba no inventário/peso.
        /// </summary>
        internal static ItemDrop FindDrop(Vector3 origin, LootKill kill, Inventory inventory)
        {
            if (kill == null || inventory == null)
            {
                return null;
            }

            // Antes de chegar ao corpo, só olhamos o raio do abate; depois de já ter visto um item,
            // vale acompanhar um drop que rolou para longe (até a distância máxima).
            float look = kill.Arrived ? Radius * 2f : Radius;
            float search = kill.SawDrop ? Mathf.Max(look, MaxDistance) : look;

            int count = Physics.OverlapSphereNonAlloc(
                kill.Position, search, s_buffer, ItemMask(), QueryTriggerInteraction.Collide);
            count = Mathf.Min(count, s_buffer.Length);

            kill.BlockedBySpace = false;

            ItemDrop best = null;
            float bestDistance = float.MaxValue;
            for (int i = 0; i < count; i++)
            {
                ItemDrop drop = DropOf(s_buffer[i]);
                if (drop == null || !drop.m_autoPickup)
                {
                    continue;
                }

                int id = drop.gameObject.GetInstanceID();
                if (kill.Before.Contains(id) || kill.Skipped.Contains(id))
                {
                    continue;
                }

                if (drop.InTar())
                {
                    kill.Skipped.Add(id);
                    continue;
                }

                bool seen = kill.Seen.Contains(id);
                if (!seen && Vector3.Distance(drop.transform.position, kill.Position) > look)
                {
                    continue;
                }

                kill.Seen.Add(id);

                ItemDrop.ItemData item = drop.m_itemData;
                if (item == null || item.m_shared == null)
                {
                    kill.Skipped.Add(id);
                    continue;
                }

                if (!inventory.CanAddItem(item, 1))
                {
                    kill.BlockedBySpace = true;
                    continue;
                }

                if (OverWeight(inventory, item))
                {
                    continue;
                }

                float distance = Vector3.Distance(origin, drop.transform.position);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = drop;
                }
            }

            return best;
        }

        /// <summary>
        /// Acompanha o <b>corpo</b> (ragdoll) do abate: os itens só saem quando ele desaparece
        /// (<c>Ragdoll.DestroyNow</c>), e o corpo pode escorregar ribanceira abaixo. Atualiza a
        /// posição para onde o corpo foi parar e anota se ele ainda existe (é o que permite encerrar
        /// cedo quando não caiu nada). Roda no máximo 1×/s e só com um abate em andamento.
        ///
        /// <para>
        /// Depois que <b>vimos</b> algum item, o corpo já soltou o que tinha — a sondagem para: além
        /// de economizar, evita que o ragdoll de <i>outra</i> criatura por perto mantenha este abate
        /// "esperando" sem motivo.
        /// </para>
        /// </summary>
        internal static void TrackBody(LootKill kill)
        {
            if (kill == null)
            {
                return;
            }

            if (kill.SawDrop)
            {
                kill.BodyChecked = true;
                kill.Body = false;
                return;
            }

            if (Time.time < s_bodyCheckAt)
            {
                return;
            }

            s_bodyCheckAt = Time.time + BodyCheckInterval;
            kill.BodyChecked = true;
            kill.Body = false;

            Ragdoll[] ragdolls = UnityEngine.Object.FindObjectsByType<Ragdoll>(
                FindObjectsInactive.Exclude, FindObjectsSortMode.None);

            float range = Radius + BodyScanSlack;
            for (int i = 0; i < ragdolls.Length; i++)
            {
                Ragdoll ragdoll = ragdolls[i];
                if (ragdoll == null || !ragdoll.m_dropItems)
                {
                    continue;
                }

                Vector3 body = ragdoll.GetAverageBodyPosition();
                if (Vector3.Distance(body, kill.Position) > range)
                {
                    continue;
                }

                kill.Body = true;
                kill.Position = body;
                break;
            }
        }

        /// <summary>Item no chão já catável (fora do piche) — usado para descartar alvo obsoleto.</summary>
        internal static bool IsStillValidTarget(ItemDrop drop)
        {
            return drop != null && drop.gameObject != null && !drop.InTar();
        }

        /// <summary>O item já está ao alcance da mão (mesma medida do auto-pickup do jogador)?</summary>
        internal static bool IsWithinReach(Vector3 me, Vector3 target)
        {
            Vector3 flat = me - target;
            flat.y = 0f;
            return flat.magnitude <= PickupReach && Mathf.Abs(me.y - target.y) <= PickupVertical;
        }

        /// <summary>Já chegamos ao ponto do abate (a ponto de parar e esperar os itens)?</summary>
        internal static bool IsAtSpot(Vector3 me, Vector3 spot)
        {
            Vector3 flat = me - spot;
            flat.y = 0f;
            return flat.magnitude <= ArriveDistance;
        }

        /// <summary>
        /// Mede o progresso da aproximação do local do abate (mesma ideia do
        /// <c>CompanionHarvest.ReportDistance</c>): enquanto a distância cai, o companheiro está
        /// "trabalhando"; parado, ele pode estar preso numa parede/água.
        /// </summary>
        internal static void ReportDistance(LootKill kill, float distance)
        {
            if (kill == null)
            {
                return;
            }

            if (kill.LastDistance < 0f || distance < kill.LastDistance - 0.5f)
            {
                kill.LastDistance = distance;
                kill.LastProgressAt = Time.time;
                return;
            }

            if (distance < kill.LastDistance)
            {
                kill.LastDistance = distance;
            }
        }

        /// <summary>
        /// True quando não conseguimos chegar ao local do abate (sem reduzir a distância por
        /// <see cref="NoProgressSeconds"/>) — evita o companheiro empurrando uma parede por 45 s.
        /// </summary>
        internal static bool IsStuck(LootKill kill)
        {
            return kill != null && !kill.Arrived &&
                   Time.time - kill.LastProgressAt > NoProgressSeconds;
        }

        // ---------------------------------------------------------------------
        // Regras de desistência / conclusão
        // ---------------------------------------------------------------------

        /// <summary>
        /// True quando vale abandonar o abate atual: longe demais, o jogador seguiu em frente, a
        /// carga estourou o teto de peso ou já demoramos demais. Sempre com limite — o companheiro
        /// não pode "vagar para sempre" atrás de um corpo.
        /// </summary>
        internal static bool ShouldGiveUp(CompanionAI companion, LootKill kill, Inventory inventory, out string reason)
        {
            reason = null;

            if (companion == null || kill == null)
            {
                reason = "sem companheiro";
                return true;
            }

            if (Vector3.Distance(companion.transform.position, kill.Position) > MaxDistance + 5f)
            {
                reason = $"local a mais de {MaxDistance + 5f:0} m";
                return true;
            }

            GameObject follow = companion.FollowTarget;
            if (follow != null &&
                Vector3.Distance(follow.transform.position, kill.Position) > MaxDistance)
            {
                reason = "o jogador seguiu em frente";
                return true;
            }

            if (IsOverWeight(inventory))
            {
                reason = $"carga acima de {MaxWeight:0} kg";
                return true;
            }

            return false;
        }

        /// <summary>
        /// True quando o abate está resolvido: pegamos tudo (e o corpo já saiu), nada caiu, ou
        /// estourou a espera/teto de tempo.
        /// </summary>
        internal static bool ShouldFinish(LootKill kill, out string reason)
        {
            reason = null;

            if (kill == null)
            {
                reason = "sem abate";
                return true;
            }

            // 1) Vimos drops, não achamos mais nada e o corpo já saiu.
            if (kill.SawDrop && kill.BodyChecked && !kill.Body &&
                Time.time - kill.LastDrop > SeenDropSettleSeconds)
            {
                reason = "peguei tudo que caiu";
                return true;
            }

            // 2) Nada caiu: corpo já saiu e demos uma folga depois de chegar.
            if (!kill.SawDrop && kill.BodyChecked && !kill.Body && kill.Arrived &&
                Time.time - kill.ArrivedAt > NoDropGraceSeconds &&
                Time.time - kill.KillTime > NoDropMinAgeSeconds)
            {
                reason = $"nada caiu (corpo já saiu; {Time.time - kill.KillTime:0} s desde o abate)";
                return true;
            }

            // 3) Tem item no chão, mas não cabe no inventário: não adianta esperar.
            if (kill.BlockedBySpace && kill.Arrived && Time.time - kill.ArrivedAt > 3f)
            {
                reason = "o inventário não tem espaço para o que caiu";
                return true;
            }

            // 4) Esperamos o tempo configurado parado no local.
            if (kill.Arrived && Time.time - kill.ArrivedAt > WaitSeconds)
            {
                reason = $"nada caiu em {WaitSeconds:0} s no local";
                return true;
            }

            // 5) Teto de tempo do trabalho inteiro.
            if (Time.time - kill.Start > MaxSeconds)
            {
                reason = $"demorei mais de {MaxSeconds:0} s";
                return true;
            }

            return false;
        }

        /// <summary>O item passaria do teto de peso configurado? (<c>0</c> = sem limite)</summary>
        internal static bool OverWeight(Inventory inventory, ItemDrop.ItemData item)
        {
            if (MaxWeight <= 0f || inventory == null || item == null)
            {
                return false;
            }

            return inventory.GetTotalWeight() + item.GetWeight() > MaxWeight;
        }

        /// <summary>O companheiro já está acima do teto de peso? (<c>0</c> = sem limite)</summary>
        internal static bool IsOverWeight(Inventory inventory)
        {
            if (MaxWeight <= 0f || inventory == null)
            {
                return false;
            }

            return inventory.GetTotalWeight() >= MaxWeight;
        }

        // ---------------------------------------------------------------------
        // Log e helpers
        // ---------------------------------------------------------------------

        /// <summary>
        /// Contabiliza um item pego do chão (é o "+1x 'LeatherScraps'" do contrato da feature) e
        /// reinicia a janela de "nada novo".
        /// </summary>
        internal static void NotifyPickedUp(string prefabName, int amount)
        {
            if (s_current != null)
            {
                s_current.SawDrop = true;
                s_current.LastDrop = Time.time;
            }

            Plugin.Log.LogInfo($"[ModIA] Loot: +{amount}x '{prefabName}'.");
        }

        /// <summary>Marca um item como descartado de vez (não será procurado de novo neste abate).</summary>
        internal static void MarkSkipped(ItemDrop drop)
        {
            if (s_current == null || drop == null)
            {
                return;
            }

            s_current.Skipped.Add(drop.gameObject.GetInstanceID());
        }

        // ---------------------------------------------------------------------
        // Log (deduplicado, como o resto do mod)
        // ---------------------------------------------------------------------

        private static string s_lastLogKey;
        private static float s_lastLogAt = -999f;

        /// <summary>
        /// Loga <b>só quando o evento muda</b>: a mesma chave não se repete dentro de 10 s. É a
        /// mesma política de <c>CompanionHarvest.LogNodeEvent</c> — sem ela, um abate que fica indo e
        /// voltando da fila (pausado por combate) viraria dezenas de linhas idênticas.
        /// </summary>
        private static void LogOnce(string key, string message)
        {
            if (key != null && key == s_lastLogKey && Time.time - s_lastLogAt < 10f)
            {
                return;
            }

            s_lastLogKey = key;
            s_lastLogAt = Time.time;
            Plugin.Log.LogInfo(message);
        }

        /// <summary>Já existe um abate (em andamento ou na fila) praticamente neste ponto?</summary>
        private static bool IsKnownKill(Vector3 position)
        {
            if (s_current != null && Vector3.Distance(s_current.Position, position) < MinKillSeparation)
            {
                return true;
            }

            for (int i = 0; i < s_queue.Count; i++)
            {
                if (Vector3.Distance(s_queue[i].Position, position) < MinKillSeparation)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Instance IDs dos <c>ItemDrop</c> já no chão em volta do ponto (foto do "antes").</summary>
        private static void CollectItemIds(Vector3 position, HashSet<int> into)
        {
            into.Clear();

            int count = Physics.OverlapSphereNonAlloc(
                position, Radius * 2f + 2f, s_buffer, ItemMask(), QueryTriggerInteraction.Collide);
            count = Mathf.Min(count, s_buffer.Length);

            for (int i = 0; i < count; i++)
            {
                ItemDrop drop = DropOf(s_buffer[i]);
                if (drop != null)
                {
                    into.Add(drop.gameObject.GetInstanceID());
                }
            }
        }

        /// <summary>
        /// <c>ItemDrop</c> a que o collider pertence. Mesmo caminho do jogador: o item é um
        /// <c>Rigidbody</c> com o <see cref="ItemDrop"/> na raiz (o fallback por pai cobre colliders
        /// filhos).
        /// </summary>
        private static ItemDrop DropOf(Collider collider)
        {
            if (collider == null)
            {
                return null;
            }

            Rigidbody body = collider.attachedRigidbody;
            ItemDrop drop = body != null ? body.GetComponent<ItemDrop>() : null;
            if (drop == null)
            {
                drop = collider.GetComponentInParent<ItemDrop>();
            }

            if (drop == null)
            {
                return null;
            }

            ZNetView netView = drop.GetComponent<ZNetView>();
            return netView != null && netView.IsValid() ? drop : null;
        }

        /// <summary>Máscara da camada <c>item</c> (a mesma que o auto-pickup do jogador usa).</summary>
        private static int ItemMask()
        {
            if (s_itemMask == -1)
            {
                s_itemMask = LayerMask.GetMask("item");
            }

            return s_itemMask;
        }

        /// <summary>Nome localizado de um token do jogo (ex.: <c>$enemy_boar</c> → "Javali").</summary>
        private static string Loc(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return "?";
            }

            try
            {
                Localization localization = Localization.instance;
                return localization != null ? localization.Localize(name) : name;
            }
            catch (Exception)
            {
                return name;
            }
        }
    }
}

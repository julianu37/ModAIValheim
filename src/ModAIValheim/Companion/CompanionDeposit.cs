using System.Collections.Generic;
using BepInEx.Configuration;
using UnityEngine;

namespace ModAIValheim.Companion
{
    /// <summary>O que o companheiro foi fazer num baú (Fase 2.14).</summary>
    internal enum ChestTripKind
    {
        /// <summary>Nenhuma viagem em andamento.</summary>
        None,

        /// <summary>Levar a carga (materiais) para o baú e voltar ao trabalho.</summary>
        Deposit,

        /// <summary>Buscar num baú a ferramenta que falta (tool keeper).</summary>
        FetchTool,

        /// <summary>Devolver ao baú a ferramenta emprestada que ele não usa mais (tool keeper).</summary>
        ReturnTools,
    }

    /// <summary>
    /// <b>Depósito em baú</b> do companheiro (Fase 2.14 — item #7 da
    /// <c>docs/audit-vs-offline-companions.md</c>, com a lógica de
    /// <c>MHR_GatherRules.DropOff</c>/<c>HandleOverweight</c>/<c>PackFull</c> e de
    /// <c>CompanionAI.PendingDeposit</c> da referência <i>Offline Companions Reborn</i> (MIT) — ver
    /// <c>THIRD_PARTY_NOTICES.md</c>).
    ///
    /// <para>
    /// <b>Problema:</b> quando o inventário enchia, a coleta simplesmente <b>encerrava</b>
    /// ("não consegui pegar o item (inventário cheio?)") — o companheiro parava de trabalhar e a
    /// tarefa morria.
    /// </para>
    ///
    /// <para>
    /// <b>Solução:</b> uma <b>viagem ao baú</b> com três finalidades
    /// (<see cref="ChestTripKind"/>). Ao estourar o peso (<c>Companheiro/PesoMaximo</c>) ou ao faltar
    /// espaço, o companheiro procura o baú utilizável mais próximo (<see cref="FindNearestChest"/>),
    /// <b>anda até ele</b>, <b>deposita</b> os materiais e <b>volta ao trabalho</b> — a tarefa de
    /// coleta continua viva (o recurso e a quantidade são os mesmos). Sem baú no raio, ele
    /// <b>pausa</b> e avisa no HUD (a tarefa <b>não</b> é perdida).
    /// </para>
    ///
    /// <para>
    /// <b>Regras do baú</b> (mesmo filtro de <c>MHR_Chests.Usable</c>): tem de ser uma
    /// <see cref="Container"/> de <c>Piece</c> (não carroça/navio/lápide/mochila), com
    /// <see cref="ZNetView"/> válida, <b>não</b> estar em uso (ninguém com ele aberto), passar no
    /// <see cref="PrivateArea.CheckAccess"/> (a mesma regra das portas) e, se for privado, ter sido
    /// construído pelo jogador dono.
    /// </para>
    ///
    /// <para>
    /// <b>Este tipo guarda estado e política</b> (qual baú, qual ferramenta, prazos, pausa). O
    /// comportamento por frame (andar/depositar) fica em <c>CompanionAI.UpdateDeposit</c>, que é quem
    /// roda no dono do ZDO — o mesmo modelo do resto do mod.
    /// </para>
    ///
    /// <para>
    /// <b>Performance:</b> a varredura de baús (<c>Piece.GetAllPiecesInRadius</c>, que percorre as
    /// peças do mundo) só roda ao <b>iniciar</b> uma viagem e tem <b>espera mínima</b>
    /// (<see cref="SearchCooldown"/>) entre tentativas; os buffers são reaproveitados e nada é
    /// alocado por frame.
    /// </para>
    /// </summary>
    internal static class CompanionDeposit
    {
        /// <summary>Distância (m) do baú em que consideramos que o companheiro chegou.</summary>
        internal const float UseReach = 2.5f;

        /// <summary>Teto (s) de uma viagem inteira (nunca deixa o companheiro vagando).</summary>
        private const float TripTimeout = 60f;

        /// <summary>Segundos sem reduzir a distância ao baú antes de desistir de chegar lá.</summary>
        private const float NoProgressTimeout = 10f;

        /// <summary>Espera (s) mínima entre varreduras de baú (a varredura percorre as peças do mundo).</summary>
        private const float SearchCooldown = 3f;

        /// <summary>Tempo (s) em que um baú que não deu para alcançar fica de fora da busca.</summary>
        private const float AvoidChestSeconds = 60f;

        /// <summary>Capacidade do buffer de peças da varredura (reaproveitado).</summary>
        private const int PieceBufferSize = 256;

        // ---------------------------------------------------------------------
        // Configuração
        // ---------------------------------------------------------------------

        private static ConfigEntry<bool> _depositWhenFull;
        private static ConfigEntry<float> _chestRadius;

        /// <summary>Depositar num baú ao encher (config <c>Coleta/DepositarAoEncher</c>).</summary>
        internal static bool Enabled => _depositWhenFull == null || _depositWhenFull.Value;

        /// <summary>Raio (m) em que procuramos um baú (config <c>Coleta/RaioBuscaBau</c>).</summary>
        internal static float ChestRadius => _chestRadius != null ? Mathf.Clamp(_chestRadius.Value, 5f, 100f) : 30f;

        internal static void Configure(ConfigEntry<bool> depositWhenFull, ConfigEntry<float> chestRadius)
        {
            _depositWhenFull = depositWhenFull;
            _chestRadius = chestRadius;

            Plugin.Log.LogInfo(
                $"[ModIA] Depósito: depositar ao encher = {Enabled} | " +
                $"raio de busca de baú = {ChestRadius:0} m.");
        }

        // ---------------------------------------------------------------------
        // Estado da viagem (o companheiro é único, como no resto do mod)
        // ---------------------------------------------------------------------

        private static ChestTripKind _kind = ChestTripKind.None;
        private static Container _chest;
        private static string _prefab;
        private static float _startedAt;
        private static float _lastProgressAt;
        private static float _lastDistance = -1f;
        private static int _moved;
        private static float _nextSearchAt = -999f;

        /// <summary>Baú que falhou (não deu para chegar) e até quando ele fica fora da busca.</summary>
        private static int _avoidChestId;
        private static float _avoidUntil = -999f;

        /// <summary>Buffer reaproveitado da varredura de baús.</summary>
        private static readonly List<Piece> s_pieces = new List<Piece>(PieceBufferSize);

        /// <summary>Instance IDs dos baús já avaliados na varredura atual (evita duplicar).</summary>
        private static readonly HashSet<int> s_seen = new HashSet<int>();

        /// <summary>Existe uma viagem ao baú em andamento?</summary>
        internal static bool TripActive => _kind != ChestTripKind.None;

        /// <summary>Finalidade da viagem em andamento.</summary>
        internal static ChestTripKind TripKind => _kind;

        /// <summary>Baú-alvo da viagem em andamento (null quando não há viagem).</summary>
        internal static Container Chest => _chest;

        /// <summary>Prefab da ferramenta envolvida (viagens de ferramenta).</summary>
        internal static string Prefab => _prefab;

        /// <summary>Quantos itens já foram movidos na viagem (log/HUD).</summary>
        internal static int Moved => _moved;

        /// <summary>Solta o estado estático (unload do plugin, dismiss, morte do companheiro).</summary>
        internal static void Shutdown()
        {
            _kind = ChestTripKind.None;
            _chest = null;
            _prefab = null;
            _moved = 0;
            _lastDistance = -1f;
            _nextSearchAt = -999f;
            _avoidChestId = 0;
            _avoidUntil = -999f;
            s_pieces.Clear();
            s_seen.Clear();
        }

        // ---------------------------------------------------------------------
        // Início das viagens
        // ---------------------------------------------------------------------

        /// <summary>
        /// Tenta levar a carga ao baú utilizável mais próximo (peso estourado ou inventário cheio).
        /// Devolve <c>false</c> quando o depósito está desligado, já existe viagem, o companheiro
        /// está em combate, não há nada que dê para guardar ou <b>não há baú no raio</b>.
        /// </summary>
        internal static bool TryStartDepositTrip(CompanionAI companion, Inventory inventory)
        {
            if (!Enabled || TripActive || companion == null || inventory == null)
            {
                return false;
            }

            if (companion.GetTargetCreature() != null)
            {
                return false;
            }

            if (!CompanionInventory.HasStorableResource(inventory))
            {
                return false;
            }

            Container chest = FindNearestChest(companion.transform.position, ChestRadius, null);
            if (chest == null)
            {
                return false;
            }

            float distance = Vector3.Distance(companion.transform.position, chest.transform.position);
            Begin(ChestTripKind.Deposit, chest, null);
            Plugin.Log.LogInfo(
                $"[ModIA] Depósito: o companheiro está cheio; levando a carga para " +
                $"'{ChestName(chest)}' a {distance:0.#} m (ele volta a coletar depois).");
            CompanionHarvest.ShowMessage(
                $"Companheiro cheio: levando a carga ao baú ({distance:0} m)", MessageHud.MessageType.TopLeft);
            return true;
        }

        /// <summary>
        /// Tenta buscar num baú próximo a ferramenta que falta (tool keeper). <paramref name="prefab"/>
        /// é o que foi encontrado (a viagem leva o companheiro até ele).
        /// </summary>
        internal static bool TryStartToolFetchTrip(CompanionAI companion, Container chest, string prefab)
        {
            // O tool keeper é ligado por Coleta/GuardarFerramentaNaoUsada (é a mesma família de
            // recursos: usar baú para a ferramenta). O depósito da carga é outro interruptor.
            if (!CompanionToolKeeper.StoreUnused || TripActive || companion == null ||
                chest == null || string.IsNullOrEmpty(prefab))
            {
                return false;
            }

            if (companion.GetTargetCreature() != null)
            {
                return false;
            }

            float distance = Vector3.Distance(companion.transform.position, chest.transform.position);
            Begin(ChestTripKind.FetchTool, chest, prefab);
            Plugin.Log.LogInfo(
                $"[ModIA] Tool keeper: indo buscar '{prefab}' no baú '{ChestName(chest)}' " +
                $"a {distance:0.#} m.");
            return true;
        }

        /// <summary>
        /// Tenta devolver ao baú as ferramentas <b>emprestadas</b> que o companheiro não usa mais
        /// (tool keeper, config <c>Coleta/GuardarFerramentaNaoUsada</c>).
        /// </summary>
        internal static bool TryStartReturnTrip(CompanionAI companion, Inventory inventory)
        {
            if (!CompanionToolKeeper.StoreUnused || !CompanionToolKeeper.HasBorrowed ||
                TripActive || companion == null || inventory == null)
            {
                return false;
            }

            if (companion.GetTargetCreature() != null)
            {
                return false;
            }

            // Alvo: o baú de onde veio (se ainda existir e tiver espaço) ou o mais próximo com espaço.
            ItemDrop.ItemData item = CompanionToolKeeper.FirstBorrowedItem(inventory);
            if (item == null)
            {
                CompanionToolKeeper.ClearBorrowed();
                return false;
            }

            Container chest = CompanionToolKeeper.ChestForReturn(inventory, item);
            if (chest == null)
            {
                return false;
            }

            Begin(ChestTripKind.ReturnTools, chest, null);
            Plugin.Log.LogInfo(
                $"[ModIA] Tool keeper: indo devolver ferramenta emprestada no baú '{ChestName(chest)}'.");
            return true;
        }

        private static void Begin(ChestTripKind kind, Container chest, string prefab)
        {
            _kind = kind;
            _chest = chest;
            _prefab = prefab;
            _moved = 0;
            _startedAt = Time.time;
            _lastProgressAt = Time.time;
            _lastDistance = -1f;
        }

        /// <summary>Encerra a viagem em andamento (com motivo no log). Sem viagem não faz nada.</summary>
        internal static void End(string reason)
        {
            if (!TripActive)
            {
                return;
            }

            ChestTripKind kind = _kind;
            int moved = _moved;
            _kind = ChestTripKind.None;
            _chest = null;
            _prefab = null;
            _moved = 0;
            _lastDistance = -1f;

            Plugin.Log.LogInfo(
                $"[ModIA] Depósito: fim da viagem ao baú ({reason}); {moved} item(ns) movido(s).");
        }

        /// <summary>Descarta a viagem e evita varrer baú de novo por um instante (falha/combate).</summary>
        internal static void Cancel(string reason)
        {
            if (!TripActive)
            {
                return;
            }

            // Um baú que não deu para alcançar fica de fora por um tempo: sem isso o companheiro
            // tentaria o MESMO baú a cada 3 s (parede/água/barranco) para sempre.
            if (_chest != null)
            {
                _avoidChestId = _chest.GetInstanceID();
                _avoidUntil = Time.time + AvoidChestSeconds;
            }

            Plugin.Log.LogWarning($"[ModIA] Depósito: viagem ao baú cancelada ({reason}).");
            End(reason);
            _nextSearchAt = Time.time + SearchCooldown;
        }

        // ---------------------------------------------------------------------
        // Andamento da viagem (chamado pelo CompanionAI)
        // ---------------------------------------------------------------------

        /// <summary>Mede o progresso da aproximação do baú (mesma ideia do loot/coleta).</summary>
        internal static void ReportDistance(float distance)
        {
            if (_lastDistance < 0f || distance < _lastDistance - 0.5f)
            {
                _lastDistance = distance;
                _lastProgressAt = Time.time;
                return;
            }

            if (distance < _lastDistance)
            {
                _lastDistance = distance;
            }
        }

        /// <summary>
        /// True quando a viagem passou do teto de tempo ou não consegue chegar ao baú (parede, água,
        /// barranco): desiste em vez de empurrar a parede para sempre.
        /// </summary>
        internal static bool IsStuck()
        {
            return TripActive &&
                   (Time.time - _startedAt > TripTimeout ||
                    Time.time - _lastProgressAt > NoProgressTimeout);
        }

        // ---------------------------------------------------------------------
        // Trabalho no baú
        // ---------------------------------------------------------------------

        /// <summary>
        /// Deposita no baú os materiais do companheiro (ver
        /// <see cref="CompanionInventory.IsStorableResource"/>): equipamento, ferramenta, arma,
        /// comida e munição ficam com ele. Devolve quantos itens saíram do inventário.
        /// </summary>
        internal static int DepositResources(Inventory inventory)
        {
            if (inventory == null || _chest == null)
            {
                return 0;
            }

            Inventory chestInventory = _chest.GetInventory();
            if (chestInventory == null)
            {
                return 0;
            }

            List<ItemDrop.ItemData> items = inventory.GetAllItems();
            int moved = 0;
            for (int i = 0; i < items.Count; i++)
            {
                ItemDrop.ItemData item = items[i];
                if (!CompanionInventory.IsStorableResource(item))
                {
                    continue;
                }

                if (!chestInventory.CanAddItem(item, item.m_stack))
                {
                    continue;
                }

                // MoveItemToThis cuida do "adiciona no destino e remove da origem" e o
                // Container salva o conteúdo no ZDO sozinho (m_onChanged -> Save).
                chestInventory.MoveItemToThis(inventory, item);
                moved++;
            }

            return moved;
        }

        /// <summary>Tira do baú o item <paramref name="prefab"/> e coloca no inventário do companheiro.</summary>
        internal static bool TakeItemFromChest(Inventory inventory, string prefab)
        {
            if (inventory == null || _chest == null || string.IsNullOrEmpty(prefab))
            {
                return false;
            }

            Inventory chestInventory = _chest.GetInventory();
            if (chestInventory == null)
            {
                return false;
            }

            ItemDrop.ItemData item = CompanionInventory.FindItem(chestInventory, prefab);
            if (item == null)
            {
                return false;
            }

            if (!inventory.CanAddItem(item, item.m_stack))
            {
                return false;
            }

            chestInventory.MoveItemToThis(inventory, item);
            return true;
        }

        /// <summary>
        /// Devolve ao baú (<paramref name="chest"/>) as ferramentas emprestadas que estiverem no
        /// inventário. Devolve quantas voltaram.
        /// </summary>
        internal static int ReturnBorrowedTools(Inventory inventory, Container chest)
        {
            if (inventory == null || chest == null)
            {
                return 0;
            }

            Inventory chestInventory = chest.GetInventory();
            if (chestInventory == null)
            {
                return 0;
            }

            int moved = 0;
            List<string> prefabs = CompanionToolKeeper.BorrowedPrefabs();
            for (int i = 0; i < prefabs.Count; i++)
            {
                ItemDrop.ItemData item = CompanionInventory.FindItem(inventory, prefabs[i]);
                if (item == null)
                {
                    continue;
                }

                if (!chestInventory.CanAddItem(item, item.m_stack))
                {
                    continue;
                }

                chestInventory.MoveItemToThis(inventory, item);
                CompanionToolKeeper.Forget(prefabs[i]);
                moved++;
            }

            return moved;
        }

        /// <summary>Registra quantos itens a viagem moveu (log do fim).</summary>
        internal static void RegisterMoved(int amount)
        {
            if (amount > 0)
            {
                _moved += amount;
            }
        }

        // ---------------------------------------------------------------------
        // Varredura de baús
        // ---------------------------------------------------------------------

        /// <summary>
        /// Baú utilizável mais próximo de <paramref name="origin"/> dentro de
        /// <paramref name="radius"/>. Com <paramref name="requiredItem"/> o baú precisa ter espaço
        /// para esse item (senão basta ter um slot livre). A varredura percorre as peças do mundo,
        /// então roda só ao iniciar uma viagem e respeita <see cref="SearchCooldown"/>.
        /// </summary>
        internal static Container FindNearestChest(Vector3 origin, float radius, ItemDrop.ItemData requiredItem)
        {
            if (Time.time < _nextSearchAt)
            {
                return null;
            }

            _nextSearchAt = Time.time + SearchCooldown;

            s_pieces.Clear();
            Piece.GetAllPiecesInRadius(origin, radius, s_pieces);

            s_seen.Clear();
            Container best = null;
            float bestDistance = float.MaxValue;

            for (int i = 0; i < s_pieces.Count; i++)
            {
                Piece piece = s_pieces[i];
                if (piece == null)
                {
                    continue;
                }

                Container container = piece.GetComponentInChildren<Container>();
                if (container == null || !s_seen.Add(container.GetInstanceID()))
                {
                    continue;
                }

                if (!IsUsableChest(container))
                {
                    continue;
                }

                // Baú que acabou de falhar (não deu para chegar) fica de fora por um tempo.
                if (container.GetInstanceID() == _avoidChestId && Time.time < _avoidUntil)
                {
                    continue;
                }

                Inventory chestInventory = container.GetInventory();
                if (requiredItem != null)
                {
                    if (!chestInventory.CanAddItem(requiredItem, requiredItem.m_stack))
                    {
                        continue;
                    }
                }
                else if (!chestInventory.HaveEmptySlot())
                {
                    // Sem item específico: o baú precisa ter onde guardar alguma coisa.
                    continue;
                }

                float distance = Vector3.Distance(origin, container.transform.position);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = container;
                }
            }

            return best;
        }

        /// <summary>
        /// Baú que o companheiro pode usar (mesmo filtro do <c>MHR_Chests.Usable</c> da referência):
        /// é um <see cref="Container"/> de construção (<c>Piece</c>), com <see cref="ZNetView"/>
        /// válida, não está em uso, passa na área privada e, sendo privado, foi construído pelo
        /// jogador dono. Nunca é carroça/navio (vagonete), lápide ou "mochila" de personagem.
        /// </summary>
        internal static bool IsUsableChest(Container container)
        {
            if (container == null || container.GetInventory() == null)
            {
                return false;
            }

            if (container.GetComponent<Character>() != null ||
                container.GetComponentInParent<Character>() != null)
            {
                return false;
            }

            if (container.GetComponent<TombStone>() != null)
            {
                return false;
            }

            if (container.GetComponentInParent<Vagon>() != null ||
                container.GetComponentInParent<Ship>() != null)
            {
                return false;
            }

            ZNetView netView = container.GetComponent<ZNetView>();
            if (netView == null || !netView.IsValid())
            {
                return false;
            }

            // Alguém com o baú aberto (nós inclusive): não mexemos.
            if (container.IsInUse())
            {
                return false;
            }

            // Mesma regra das portas (#5): área privada sem acesso não é nossa. Com o parâmetro
            // `wardCheck: false` a checagem é "existe um escudo de guarda aqui que o jogador local
            // não pode acessar?" — sem escudo nenhum ela devolve `true` (o caso comum).
            if (!PrivateArea.CheckAccess(container.transform.position, 0f, false, false))
            {
                return false;
            }

            if (container.m_privacy == Container.PrivacySetting.Private)
            {
                Piece piece = container.GetComponentInParent<Piece>();
                if (piece == null || piece.GetCreator() != LocalPlayerId())
                {
                    return false;
                }
            }

            return true;
        }

        private static long LocalPlayerId()
        {
            Player player = Player.m_localPlayer;
            return player != null ? player.GetPlayerID() : 0L;
        }

        /// <summary>Nome localizado do baú (o <c>m_name</c> é um token, ex.: <c>$piece_chest_wood</c>).</summary>
        internal static string ChestName(Container chest)
        {
            if (chest == null)
            {
                return "?";
            }

            string name = chest.m_name;
            if (string.IsNullOrEmpty(name))
            {
                return chest.gameObject.name;
            }

            Localization localization = Localization.instance;
            if (localization == null)
            {
                return name;
            }

            string localized = localization.Localize(name);
            return string.IsNullOrEmpty(localized) ? name : localized;
        }

        /// <summary>Lista legível do estado do depósito (comando <c>modia_stats</c>).</summary>
        internal static string Describe()
        {
            if (!TripActive)
            {
                return Enabled
                    ? $"nenhuma viagem (raio {ChestRadius:0} m)"
                    : "desligado";
            }

            Container chest = _chest;
            string where = chest != null
                ? $"'{ChestName(chest)}' a {Vector3.Distance(chest.transform.position, CompanionManagerPosition()):0.#} m"
                : "baú sumiu";
            return $"{_kind} → {where}, {_moved} item(ns) movido(s)";
        }

        private static Vector3 CompanionManagerPosition()
        {
            CompanionAI companion = CompanionManager.ActiveCompanion;
            return companion != null ? companion.transform.position : Vector3.zero;
        }
    }
}

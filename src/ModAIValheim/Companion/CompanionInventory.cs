using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using UnityEngine;

namespace ModAIValheim.Companion
{
    /// <summary>
    /// Inventário e equipamento do companheiro (Fase 2.3).
    ///
    /// <para>
    /// <b>Compatibilidade com o Valheim 1.0 (item A1 do <c>docs/re/api-diff.md</c>):</b> o
    /// <see cref="VisEquipment"/> deixou de aceitar NOMES de prefab e passou a trabalhar com
    /// <b>hash do prefab + quality</b> (<c>SetRightItem(int itemHash, int quality)</c>,
    /// <c>SetLeftItem(int itemHash, int variant, int quality)</c>,
    /// <c>SetChestItem(int itemHash)</c>, <c>SetShoulderItem(int itemHash, int variant, int quality)</c>, …).
    /// O hash tem de ser o mesmo que o jogo usa em <c>Humanoid.SetupVisEquipment</c>, isto é
    /// <c>Utils.GetPrefabName(prefab).GetStableHashCode()</c> (a extensão
    /// <c>GetStableHashCode()</c> mora em <c>assembly_utils</c>).
    /// </para>
    ///
    /// <para>
    /// <b>Fluxo de equipar:</b> criar o item a partir do prefab de <see cref="ObjectDB"/>,
    /// colocar no inventário do <see cref="Humanoid"/> (o <c>EquipItem</c> recusa itens que não
    /// estejam no inventário) e chamar <c>Humanoid.EquipItem</c>. Ao final do <c>EquipItem</c> o
    /// jogo chama <c>SetupEquipment()</c> → <c>SetupVisEquipment()</c>, que já grava os hashes no
    /// ZDO. Mesmo assim reaplicamos o slot explicitamente (<see cref="ApplySlot"/>) para cobrir o
    /// caso "item já estava equipado" e para não depender da ordem de <c>Awake</c>/ZDO.
    /// </para>
    ///
    /// <para>
    /// Nada aqui roda por frame: só em spawn, comando de console ou chamada explícita.
    /// </para>
    /// </summary>
    internal static class CompanionInventory
    {
        /// <summary>Separador da lista de armaduras da config.</summary>
        private const char ListSeparator = ',';

        private static ConfigEntry<bool> _equipOnSpawn;
        private static ConfigEntry<string> _weaponPrefab;
        private static ConfigEntry<string> _armorPrefabs;

        // Ferramenta de coleta fornecida quando falta (Fase 2.8).
        private static ConfigEntry<bool> _provideTools;
        private static ConfigEntry<string> _providedToolPrefabs;

        // Peso máximo do companheiro (Fase 2.14).
        private static ConfigEntry<float> _maxWeight;

        private static bool _visualsInspected;

        internal static void Configure(
            ConfigEntry<bool> equipOnSpawn,
            ConfigEntry<string> weaponPrefab,
            ConfigEntry<string> armorPrefabs)
        {
            _equipOnSpawn = equipOnSpawn;
            _weaponPrefab = weaponPrefab;
            _armorPrefabs = armorPrefabs;
        }

        /// <summary>
        /// Config da <b>ferramenta de coleta fornecida</b> (Fase 2.8): quando o companheiro não tem
        /// machado/picareta, o mod cria uma do conjunto configurado e coloca no inventário — é o que
        /// faz pedra/minério funcionarem sem o jogador ter de equipar nada à mão.
        /// </summary>
        internal static void ConfigureHarvestTools(
            ConfigEntry<bool> provideTools,
            ConfigEntry<string> toolPrefabs)
        {
            _provideTools = provideTools;
            _providedToolPrefabs = toolPrefabs;

            Plugin.Log.LogInfo(
                $"[ModIA] Coleta: fornecer ferramenta quando faltar = {ProvideTools} | " +
                $"ferramentas = '{ProvidedToolPrefabs}'.");
        }

        /// <summary>Solta o estado estático (unload do plugin / fim do mundo).</summary>
        internal static void Shutdown()
        {
            _visualsInspected = false;
            s_missingToolWarned.Clear();
        }

        // ---------------------------------------------------------------------
        // Peso / carga (Fase 2.14)
        // ---------------------------------------------------------------------

        /// <summary>
        /// Config do <b>peso máximo</b> do companheiro (Fase 2.14, item #7 da auditoria). Antes de
        /// catar/coletar o companheiro soma o peso do item ao que já carrega e <b>nunca</b> passa do
        /// teto; ao estourar ele leva a carga ao baú e retoma a tarefa
        /// (ver <see cref="CompanionDeposit"/>).
        /// </summary>
        internal static void ConfigureWeight(ConfigEntry<float> maxWeight)
        {
            _maxWeight = maxWeight;

            Plugin.Log.LogInfo(
                $"[ModIA] Companheiro: peso máximo = " +
                $"{(MaxWeight > 0f ? MaxWeight + " kg" : "sem limite")}.");
        }

        /// <summary>
        /// Peso máximo (kg) que o companheiro carrega (config <c>Companheiro/PesoMaximo</c>).
        /// <c>0</c> = <b>sem limite</b> (o jogo não impõe limite de peso a personagens).
        /// </summary>
        internal static float MaxWeight => _maxWeight != null ? Mathf.Max(0f, _maxWeight.Value) : 0f;

        /// <summary>Peso total (kg) que o companheiro está carregando.</summary>
        internal static float GetWeight(Inventory inventory)
        {
            return inventory != null ? inventory.GetTotalWeight() : 0f;
        }

        /// <summary>
        /// O item <paramref name="item"/> passaria do teto de peso (<c>0</c> = sem limite)? É a
        /// checagem "nunca pegar o que estoura o teto" — usada antes de catar (coleta, loot e
        /// pickables).
        /// </summary>
        internal static bool WouldExceedWeight(Inventory inventory, ItemDrop.ItemData item)
        {
            if (MaxWeight <= 0f || inventory == null)
            {
                return false;
            }

            if (item == null)
            {
                return inventory.GetTotalWeight() >= MaxWeight;
            }

            return inventory.GetTotalWeight() + item.GetWeight() > MaxWeight;
        }

        /// <summary>O companheiro já está no (ou acima do) teto de peso? (<c>0</c> = sem limite)</summary>
        internal static bool IsOverWeight(Inventory inventory)
        {
            if (MaxWeight <= 0f || inventory == null)
            {
                return false;
            }

            return inventory.GetTotalWeight() >= MaxWeight;
        }

        /// <summary>
        /// Item que o companheiro <b>guarda num baú</b>: material, troféu, peixe ou miudeza que
        /// <b>não</b> está equipado e <b>não</b> é comida. Equipamento, ferramenta, arma, munição,
        /// comida e mead ficam com ele (é o que o mantém funcionando).
        /// </summary>
        internal static bool IsStorableResource(ItemDrop.ItemData item)
        {
            if (item == null || item.m_shared == null || item.m_equipped)
            {
                return false;
            }

            if (item.m_shared.m_food > 0f || item.m_shared.m_foodStamina > 0f)
            {
                return false;
            }

            switch (item.m_shared.m_itemType)
            {
                case ItemDrop.ItemData.ItemType.Material:
                case ItemDrop.ItemData.ItemType.Trophy:
                case ItemDrop.ItemData.ItemType.Misc:
                case ItemDrop.ItemData.ItemType.Fish:
                case ItemDrop.ItemData.ItemType.AmmoNonEquipable:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>Existe algo no inventário que possa ser guardado num baú?</summary>
        internal static bool HasStorableResource(Inventory inventory)
        {
            if (inventory == null)
            {
                return false;
            }

            List<ItemDrop.ItemData> items = inventory.GetAllItems();
            for (int i = 0; i < items.Count; i++)
            {
                if (IsStorableResource(items[i]))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// O companheiro tem as duas ferramentas de coleta (algo com dano de <i>corte</i> e algo com
        /// dano de <i>picareta</i>)? É a mesma régua do <see cref="EnsureHarvestTools"/> — o tool
        /// keeper (Fase 2.14) só procura num baú quando ainda falta alguma.
        /// </summary>
        internal static bool HasHarvestTools(CompanionAI companion)
        {
            return GetMissingHarvestToolKinds(companion, out bool needChop, out bool needPickaxe) &&
                   !needChop && !needPickaxe;
        }

        /// <summary>
        /// Quais tipos de ferramenta de coleta <b>faltam</b> ao companheiro: algo com dano de
        /// <i>corte</i> (machado) e algo com dano de <i>picareta</i>. Devolve <c>false</c> quando não
        /// há inventário (aí os dois <c>out</c> saem <c>false</c> e quem chamou não age).
        /// </summary>
        internal static bool GetMissingHarvestToolKinds(
            CompanionAI companion,
            out bool needChop,
            out bool needPickaxe)
        {
            needChop = false;
            needPickaxe = false;

            Humanoid humanoid = Resolve(companion);
            Inventory inventory = humanoid != null ? humanoid.GetInventory() : null;
            if (inventory == null)
            {
                return false;
            }

            bool hasChop = false;
            bool hasPickaxe = false;
            List<ItemDrop.ItemData> items = inventory.GetAllItems();
            for (int i = 0; i < items.Count; i++)
            {
                ItemDrop.ItemData item = items[i];
                if (!IsHarvestTool(item))
                {
                    continue;
                }

                HitData.DamageTypes damage = item.m_shared.m_damages;
                if (damage.m_chop > 0f)
                {
                    hasChop = true;
                }

                if (damage.m_pickaxe > 0f)
                {
                    hasPickaxe = true;
                }
            }

            needChop = !hasChop;
            needPickaxe = !hasPickaxe;
            return true;
        }

        /// <summary>
        /// Item do inventário pelo nome do prefab (a busca que o tool keeper e o depósito usam para
        /// reencontrar um item movido). Devolve <c>null</c> quando não está lá.
        /// </summary>
        internal static ItemDrop.ItemData FindItem(Inventory inventory, string prefabName)
        {
            return FindInInventory(inventory, prefabName);
        }

        /// <summary>
        /// Garante que o companheiro tenha o item <paramref name="prefabName"/> no inventário,
        /// <b>criando</b> um (durabilidade cheia, qualidade 1) quando falta. É o mesmo caminho do
        /// <see cref="EnsureHarvestTools"/>; a Fase 2.17 usa para o <b>martelo</b> de construção
        /// (sem ele o companheiro não repara nada — o jogo exige o martelo na mão, como para o
        /// jogador). Devolve o item (o que já estava ou o recém-criado) e <c>null</c> quando o
        /// prefab não existe no <c>ObjectDB</c> ou o inventário não aceita.
        /// </summary>
        internal static ItemDrop.ItemData EnsureItem(CompanionAI companion, string prefabName)
        {
            Humanoid humanoid = Resolve(companion);
            Inventory inventory = humanoid != null ? humanoid.GetInventory() : null;
            if (inventory == null || string.IsNullOrEmpty(prefabName))
            {
                return null;
            }

            ItemDrop.ItemData existing = FindInInventory(inventory, prefabName);
            if (existing != null)
            {
                return existing;
            }

            GameObject prefab = FindItemPrefab(prefabName);
            if (prefab == null)
            {
                return null;
            }

            ItemDrop.ItemData created = AddItems(inventory, prefab, 1);
            if (created != null)
            {
                Plugin.Log.LogInfo(
                    $"[ModIA] Item fornecido ao companheiro: '{Utils.GetPrefabName(prefab.name)}'.");
            }

            return created;
        }

        // ---------------------------------------------------------------------
        // Loadout padrão
        // ---------------------------------------------------------------------

        /// <summary>
        /// Aplica o loadout configurado (<c>WeaponPrefab</c> + <c>ArmorSetPrefabs</c>) na
        /// instância recém-spawnada. Tolerante a falhas: cada item que não existir/ não couber
        /// no inventário só gera warning no log e os demais continuam.
        /// </summary>
        internal static void ApplyDefaultLoadout(CompanionAI companion)
        {
            if (companion == null)
            {
                return;
            }

            if (_equipOnSpawn == null || !_equipOnSpawn.Value)
            {
                Plugin.Log.LogInfo("[ModIA] Loadout padrão desligado na config; companheiro spawna vazio.");
                return;
            }

            string weapon = _weaponPrefab?.Value;
            if (!string.IsNullOrEmpty(weapon))
            {
                EquipWeapon(companion, weapon.Trim());
            }

            string armorList = _armorPrefabs?.Value;
            if (string.IsNullOrEmpty(armorList))
            {
                return;
            }

            string[] names = armorList.Split(ListSeparator);
            for (int i = 0; i < names.Length; i++)
            {
                string name = names[i].Trim();
                if (name.Length == 0)
                {
                    continue;
                }

                // Sem slot explícito: o slot visual sai do ItemType do próprio prefab.
                Equip(companion, name);
            }
        }

        // ---------------------------------------------------------------------
        // API de equipamento
        // ---------------------------------------------------------------------

        /// <summary>
        /// Cria (se preciso) e equipa a arma <paramref name="prefabName"/> no companheiro.
        /// A mão usada é decidida pelo próprio <see cref="Humanoid.EquipItem"/> (direita/esquerda
        /// conforme o ItemType) e o visual é sincronizado nesse mesmo slot.
        /// </summary>
        internal static bool EquipWeapon(CompanionAI companion, string prefabName)
        {
            return EquipInternal(companion, prefabName, null);
        }

        /// <summary>
        /// Cria (se preciso) e equipa a armadura <paramref name="prefabName"/> no companheiro,
        /// forçando o slot visual <paramref name="slot"/> (ex.: <see cref="VisSlot.Chest"/>,
        /// <see cref="VisSlot.Helmet"/>).
        /// </summary>
        internal static bool EquipArmor(CompanionAI companion, string prefabName, VisSlot slot)
        {
            return EquipInternal(companion, prefabName, slot);
        }

        /// <summary>
        /// Equipa o item decidindo o slot visual pelo <c>ItemType</c> do prefab (armas → mão,
        /// armaduras → peito/pernas/elmo/capa/utilitário). É o caminho usado pelo loadout e pelo
        /// comando <c>modia_equip</c> sem slot explícito.
        /// </summary>
        internal static bool Equip(CompanionAI companion, string prefabName)
        {
            return EquipInternal(companion, prefabName, null);
        }

        private static bool EquipInternal(CompanionAI companion, string prefabName, VisSlot? forcedSlot)
        {
            Humanoid humanoid = Resolve(companion);
            if (humanoid == null)
            {
                return false;
            }

            GameObject prefab = FindItemPrefab(prefabName);
            if (prefab == null)
            {
                return false;
            }

            Inventory inventory = humanoid.GetInventory();
            if (inventory == null)
            {
                Plugin.Log.LogError("[ModIA] Companheiro sem inventário; equipamento cancelado.");
                return false;
            }

            ItemDrop.ItemData item = FindInInventory(inventory, prefab);
            if (item == null)
            {
                item = AddItems(inventory, prefab, 1);
                if (item == null)
                {
                    Plugin.Log.LogWarning(
                        $"[ModIA] Não foi possível colocar '{prefab.name}' no inventário " +
                        "do companheiro (inventário cheio?).");
                    return false;
                }
            }

            if (humanoid.IsItemEquiped(item))
            {
                // Já equipado: só garante que o visual está sincronizado.
                SyncVisual(humanoid, item, forcedSlot);
                Plugin.Log.LogInfo(
                    $"[ModIA] '{prefab.name}' já estava equipado no companheiro (visual re-sincronizado).");
                return true;
            }

            // triggerEquipEffects: false — é o que o próprio jogo faz para NPCs (GiveDefaultItem),
            // evitando criar GameObjects de efeito a cada equip.
            if (!humanoid.EquipItem(item, triggerEquipEffects: false))
            {
                Plugin.Log.LogWarning(
                    $"[ModIA] Humanoid.EquipItem recusou '{prefab.name}' " +
                    "(item em uso em ataque/dodge ou regra do jogo).");
                return false;
            }

            bool visualOk = SyncVisual(humanoid, item, forcedSlot);
            Plugin.Log.LogInfo(
                $"[ModIA] Equipado '{prefab.name}' no companheiro " +
                $"(tipo: {item.m_shared.m_itemType}, qualidade: {item.m_quality}, visual: " +
                (visualOk ? "atualizado)" : "sem VisEquipment/perna de fallback)"));
            return true;
        }

        // ---------------------------------------------------------------------
        // Snapshot / restauração (Fase 2.7)
        // ---------------------------------------------------------------------

        /// <summary>
        /// Preenche <paramref name="state"/> com o inventário e o equipamento atuais do companheiro
        /// (é a metade "itens" do snapshot). Não aloca por frame: só é chamado no dismiss, na morte
        /// e no fim do mundo.
        /// </summary>
        internal static void CaptureItems(CompanionAI companion, CompanionState state)
        {
            if (state == null)
            {
                return;
            }

            state.Items.Clear();

            Humanoid humanoid = Resolve(companion);
            if (humanoid == null)
            {
                return;
            }

            Inventory inventory = humanoid.GetInventory();
            if (inventory == null)
            {
                Plugin.Log.LogWarning("[ModIA] Companheiro sem inventário; snapshot sai vazio.");
                return;
            }

            List<ItemDrop.ItemData> items = inventory.GetAllItems();
            for (int i = 0; i < items.Count; i++)
            {
                ItemDrop.ItemData item = items[i];
                if (item == null || item.m_shared == null)
                {
                    continue;
                }

                // O nome do prefab é a chave estável entre sessões (a instância do ObjectDB não
                // sobrevive ao fechar o jogo). Itens sem m_dropPrefab não podem ser recriados.
                if (item.m_dropPrefab == null)
                {
                    Plugin.Log.LogWarning(
                        $"[ModIA] Item '{item.m_shared.m_name}' sem prefab de origem; " +
                        "não entra no snapshot (não dá para recriar).");
                    continue;
                }

                string prefabName = Utils.GetPrefabName(item.m_dropPrefab);
                if (string.IsNullOrEmpty(prefabName))
                {
                    continue;
                }

                CompanionItemState record = new CompanionItemState();
                record.Prefab = prefabName;
                record.Stack = Mathf.Max(1, item.m_stack);
                record.Quality = Mathf.Max(1, item.m_quality);
                record.Durability = item.m_durability;
                record.WorldLevel = item.m_worldLevel;
                record.Variant = item.m_variant;
                record.Equipped = humanoid.IsItemEquiped(item);
                state.Items.Add(record);
            }
        }

        /// <summary>
        /// Devolve ao companheiro os itens do snapshot e reequipa os que estavam equipados.
        ///
        /// <para>
        /// Reusa o caminho normal do mod: o item é criado do <c>ObjectDB</c> por
        /// <see cref="CreateItem"/> (com pilha/qualidade/durabilidade/<c>worldLevel</c>/variante do
        /// snapshot), entra no inventário pelo <c>Inventory.AddItem</c> e, se estava equipado,
        /// passa pelo <see cref="EquipItemData"/> — que é o mesmo <c>Humanoid.EquipItem</c> +
        /// <see cref="SyncVisual"/> do <c>modia_equip</c>.
        /// </para>
        ///
        /// <para>
        /// O companheiro nasce vazio, então só <b>adicionamos</b>: não há duplicação. Se por algum
        /// motivo o inventário já tiver itens, a restauração é <b>recusada</b> (e logada) para não
        /// duplicar. Item cujo prefab não existe mais é <b>pulado com log</b>.
        /// </para>
        /// </summary>
        /// <returns>True se o inventário pôde ser restaurado (mesmo que algum item tenha sido pulado).</returns>
        internal static bool RestoreItems(CompanionAI companion, CompanionState state)
        {
            if (state == null)
            {
                return false;
            }

            Humanoid humanoid = Resolve(companion);
            if (humanoid == null)
            {
                return false;
            }

            Inventory inventory = humanoid.GetInventory();
            if (inventory == null)
            {
                Plugin.Log.LogError("[ModIA] Companheiro sem inventário; snapshot não restaurado.");
                return false;
            }

            if (inventory.NrOfItems() > 0)
            {
                Plugin.Log.LogWarning(
                    "[ModIA] O companheiro recém-invocado já tem itens no inventário; " +
                    "restauração do snapshot recusada para não duplicar.");
                return false;
            }

            List<CompanionItemState> records = state.Items;
            int added = 0;
            int equipped = 0;
            int skipped = 0;

            for (int i = 0; i < records.Count; i++)
            {
                CompanionItemState record = records[i];
                if (record == null || string.IsNullOrEmpty(record.Prefab))
                {
                    skipped++;
                    continue;
                }

                // Prefab sumiu (mod removido, item renomeado pelo jogo): loga e pula.
                GameObject prefab = FindItemPrefab(record.Prefab);
                if (prefab == null)
                {
                    Plugin.Log.LogWarning(
                        $"[ModIA] Item '{record.Prefab}' do snapshot não existe mais; pulado.");
                    skipped++;
                    continue;
                }

                ItemDrop.ItemData item = CreateItem(
                    prefab,
                    record.Stack,
                    record.Quality,
                    record.Durability,
                    record.WorldLevel,
                    record.Variant);

                if (item == null)
                {
                    skipped++;
                    continue;
                }

                if (!inventory.AddItem(item))
                {
                    Plugin.Log.LogWarning(
                        $"[ModIA] Inventário do companheiro cheio; '{record.Prefab}' do snapshot " +
                        "ficou de fora.");
                    skipped++;
                    continue;
                }

                added++;

                if (record.Equipped)
                {
                    // Itens empilháveis (ex.: flechas) podem ter sido absorvidos por um stack
                    // existente: nesse caso quem está no inventário é o stack antigo, e é ele que
                    // precisa voltar para o slot.
                    ItemDrop.ItemData target = inventory.ContainsItem(item)
                        ? item
                        : FindInInventory(inventory, record.Prefab);

                    if (target != null && EquipItemData(companion, target))
                    {
                        equipped++;
                    }
                }
            }

            Plugin.Log.LogInfo(
                $"[ModIA] Snapshot restaurado no companheiro: {added} item(ns) devolvido(s), " +
                $"{equipped} equipado(s), {skipped} pulado(s).");
            return true;
        }

        /// <summary>
        /// Equipa um <see cref="ItemDrop.ItemData"/> que já está no inventário (caminho da
        /// restauração do snapshot: não cria nada, só equipa o que o snapshot trouxe).
        /// </summary>
        private static bool EquipItemData(CompanionAI companion, ItemDrop.ItemData item)
        {
            Humanoid humanoid = Resolve(companion);
            if (humanoid == null || item == null)
            {
                return false;
            }

            if (humanoid.IsItemEquiped(item))
            {
                SyncVisual(humanoid, item, null);
                return true;
            }

            // triggerEquipEffects: false — mesma escolha do EquipInternal (é o que o jogo faz para
            // NPCs, e evita criar GameObjects de efeito).
            if (!humanoid.EquipItem(item, triggerEquipEffects: false))
            {
                Plugin.Log.LogWarning(
                    $"[ModIA] Humanoid.EquipItem recusou '{item.m_shared.m_name}' na restauração.");
                return false;
            }

            SyncVisual(humanoid, item, null);
            return true;
        }

        // ---------------------------------------------------------------------
        // Drop
        // ---------------------------------------------------------------------

        /// <summary>
        /// Dropa <paramref name="amount"/> unidades (todas se <paramref name="amount"/> ≤ 0) do item
        /// <paramref name="prefabName"/> que estiver no inventário do companheiro.
        /// Devolve quantas unidades saíram (0 se nada foi dropado).
        /// </summary>
        internal static int DropItem(CompanionAI companion, string prefabName, int amount)
        {
            Humanoid humanoid = Resolve(companion);
            if (humanoid == null)
            {
                return 0;
            }

            Inventory inventory = humanoid.GetInventory();
            ItemDrop.ItemData item = FindInInventory(inventory, prefabName);
            if (item == null)
            {
                Plugin.Log.LogWarning($"[ModIA] '{prefabName}' não está no inventário do companheiro.");
                return 0;
            }

            int count = amount <= 0 ? item.m_stack : Mathf.Min(amount, item.m_stack);
            if (count <= 0)
            {
                return 0;
            }

            // Humanoid.DropItem: desequipa (e re-sincroniza o VisEquipment) antes de criar o
            // ItemDrop no mundo — por isso não chamamos ItemDrop.DropItem direto.
            if (!humanoid.DropItem(inventory, item, count))
            {
                Plugin.Log.LogWarning(
                    $"[ModIA] O jogo recusou dropar '{prefabName}' (item de quest? já removido?).");
                return 0;
            }

            Plugin.Log.LogInfo($"[ModIA] Companheiro dropou {count}x '{prefabName}'.");
            return count;
        }

        // ---------------------------------------------------------------------
        // Ferramenta de coleta (Fase 2.4b)
        // ---------------------------------------------------------------------

        /// <summary>
        /// Buffer reaproveitado da última consulta de ferramentas (a busca de nós roda a cada
        /// 0,5 s e não pode alocar por varredura).
        /// </summary>
        private static readonly List<HarvestToolProfile> s_toolBuffer = new List<HarvestToolProfile>();

        /// <summary>Conjunto padrão de ferramentas fornecidas quando o companheiro não tem nenhuma.</summary>
        internal const string DefaultProvidedTools = "AxeStone,PickaxeAntler,PickaxeIron";

        /// <summary>Prefabs de ferramenta já avisados como inexistentes (evita spam no log).</summary>
        private static readonly HashSet<string> s_missingToolWarned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Momento do último aviso de "nenhuma ferramenta da lista atende ao nó" (rate-limited).</summary>
        private static float s_lastTierWarnAt = -999f;

        /// <summary>Fornecer ferramenta de coleta quando o companheiro não tem nenhuma (config).</summary>
        internal static bool ProvideTools => _provideTools == null || _provideTools.Value;

        /// <summary>Lista configurada de ferramentas fornecidas (config <c>Coleta/FerramentasFornecidas</c>).</summary>
        internal static string ProvidedToolPrefabs =>
            _providedToolPrefabs != null && !string.IsNullOrEmpty(_providedToolPrefabs.Value)
                ? _providedToolPrefabs.Value
                : DefaultProvidedTools;

        /// <summary>
        /// Garante que o companheiro tenha <b>alguma</b> ferramenta para o tipo de nó: sem machado
        /// (dano de <i>chop</i>) e sem picareta (dano de <i>pickaxe</i>) no inventário, cria a
        /// primeira ferramenta compatível de <see cref="ProvidedToolPrefabs"/> e coloca no
        /// inventário (sem equipar). É o que faz pedra/minério entrarem na conta de coleta mesmo com
        /// o loadout padrão (que só tem <c>Club</c>) ou com um snapshot antigo.
        ///
        /// <para>
        /// Roda no máximo uma vez por ferramenta ausente: assim que o item entra no inventário, os
        /// tipos deixam de faltar e a função não cria nada de novo. Desligável em
        /// <c>Coleta/FornecerFerramentas</c>.
        /// </para>
        /// </summary>
        internal static void EnsureHarvestTools(CompanionAI companion, string resourceName)
        {
            if (!ProvideTools)
            {
                return;
            }

            Humanoid humanoid = Resolve(companion);
            Inventory inventory = humanoid != null ? humanoid.GetInventory() : null;
            if (inventory == null)
            {
                return;
            }

            bool hasChop = false;
            bool hasPickaxe = false;
            List<ItemDrop.ItemData> items = inventory.GetAllItems();
            for (int i = 0; i < items.Count; i++)
            {
                ItemDrop.ItemData item = items[i];
                if (!IsHarvestTool(item))
                {
                    continue;
                }

                HitData.DamageTypes damage = item.m_shared.m_damages;
                if (damage.m_chop > 0f)
                {
                    hasChop = true;
                }

                if (damage.m_pickaxe > 0f)
                {
                    hasPickaxe = true;
                }
            }

            if (hasChop && hasPickaxe)
            {
                return;
            }

            string[] names = ProvidedToolPrefabs.Split(ListSeparator);
            for (int i = 0; i < names.Length; i++)
            {
                string name = names[i].Trim();
                if (name.Length == 0)
                {
                    continue;
                }

                GameObject prefab = FindToolPrefab(name);
                ItemDrop drop = prefab != null ? prefab.GetComponent<ItemDrop>() : null;
                if (drop == null || drop.m_itemData == null || drop.m_itemData.m_shared == null)
                {
                    continue;
                }

                HitData.DamageTypes damage = drop.m_itemData.m_shared.m_damages;
                bool chop = damage.m_chop > 0f;
                bool pickaxe = damage.m_pickaxe > 0f;

                // Só cria o que falta (e só serve para o tipo que ele cobre).
                if ((!chop || hasChop) && (!pickaxe || hasPickaxe))
                {
                    continue;
                }

                if (FindInInventory(inventory, prefab) != null)
                {
                    continue;
                }

                if (AddItems(inventory, prefab, 1) == null)
                {
                    continue;
                }

                if (chop)
                {
                    hasChop = true;
                }

                if (pickaxe)
                {
                    hasPickaxe = true;
                }

                Plugin.Log.LogInfo(
                    $"[ModIA] Ferramenta de coleta fornecida ao companheiro: '{name}' " +
                    $"(pedido: '{resourceName}').");
            }
        }

        /// <summary>
        /// Vara de pesca no inventário: o item com a animação de vara
        /// (<c>ItemData.AnimationState.FishingRod</c>) — é a mesma marcação que o <c>FishController</c>
        /// da referência usa. Null quando ele não tem nenhuma.
        /// </summary>
        internal static ItemDrop.ItemData FindFishingRod(Inventory inventory)
        {
            if (inventory == null)
            {
                return null;
            }

            List<ItemDrop.ItemData> items = inventory.GetAllItems();
            for (int i = 0; i < items.Count; i++)
            {
                ItemDrop.ItemData item = items[i];
                if (item != null && item.m_shared != null &&
                    item.m_shared.m_animationState == ItemDrop.ItemData.AnimationState.FishingRod)
                {
                    return item;
                }
            }

            return null;
        }

        /// <summary>
        /// Garante a <b>vara de pesca</b> e a <b>isca</b> do trabalho de pescar (Fase 2.16a), como
        /// <see cref="EnsureHarvestTools"/> faz com machado/picareta: sem vara ele não pesca, e sem
        /// isca o arremesso nem sai (o <c>Attack.Start</c> cobra a munição do tipo da vara).
        ///
        /// <para>
        /// Só cria o que falta e devolve <c>false</c> com <paramref name="missing"/> quando nem criar
        /// nem achar resolveu (prefab inexistente, inventário cheio). Desligável em
        /// <c>Jobs/FornecerVara</c> e <c>Jobs/FornecerIsca</c>.
        /// </para>
        /// </summary>
        internal static bool EnsureFishingGear(
            CompanionAI companion,
            string rodPrefab,
            string baitPrefab,
            int baitAmount,
            bool provideRod,
            bool provideBait,
            out string missing)
        {
            missing = null;

            Humanoid humanoid = Resolve(companion);
            Inventory inventory = humanoid != null ? humanoid.GetInventory() : null;
            if (inventory == null)
            {
                missing = "companheiro sem inventario";
                return false;
            }

            if (FindFishingRod(inventory) == null)
            {
                GameObject prefab = provideRod ? FindItemPrefab(rodPrefab) : null;
                if (prefab != null && AddItems(inventory, prefab, 1) != null)
                {
                    Plugin.Log.LogInfo($"[ModIA] Pescar: vara fornecida ao companheiro ('{rodPrefab}').");
                }
                else
                {
                    missing = $"sem vara de pesca ('{rodPrefab}')";
                    return false;
                }
            }

            if (FindInInventoryByName(inventory, baitPrefab) == null)
            {
                GameObject prefab = provideBait ? FindItemPrefab(baitPrefab) : null;
                int amount = Mathf.Max(1, baitAmount);
                if (prefab != null && AddItems(inventory, prefab, amount) != null)
                {
                    Plugin.Log.LogInfo(
                        $"[ModIA] Pescar: {amount}x '{baitPrefab}' fornecidos ao companheiro.");
                }
                else
                {
                    missing = $"sem isca ('{baitPrefab}')";
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Garante uma ferramenta <b>do tier exigido pelo nó</b> (item #8 da auditoria). Quando a
        /// varredura acha um nó que renderia o recurso mas a ferramenta atual não arranha — típico de
        /// prata/obsidiana (tier 2) com a picareta de chifre (tier 1) —, escolhe a <b>melhor</b>
        /// ferramenta de <c>Coleta/FerramentasFornecidas</c> que atenda (tier ≥ mínimo e dano &gt; 0
        /// contra as resistências do nó) e coloca no inventário.
        ///
        /// <para>
        /// Não cria nada quando o companheiro já tem ferramenta que atende, nem quando a lista não
        /// tem nada capaz (aí só avisa no log — o HUD continua avisando pelo
        /// <see cref="CompanionHarvest.NotifyMissingTool"/>).
        /// </para>
        /// </summary>
        /// <returns><c>true</c> quando uma ferramenta nova entrou no inventário (vale repetir a varredura).</returns>
        internal static bool EnsureHarvestToolForNode(
            CompanionAI companion,
            string resourceName,
            HitData.DamageModifiers modifiers,
            int minToolTier)
        {
            if (!ProvideTools)
            {
                return false;
            }

            Humanoid humanoid = Resolve(companion);
            Inventory inventory = humanoid != null ? humanoid.GetInventory() : null;
            if (inventory == null)
            {
                return false;
            }

            // Já existe ferramenta que atende (tier + dano contra as resistências do nó)?
            List<ItemDrop.ItemData> items = inventory.GetAllItems();
            for (int i = 0; i < items.Count; i++)
            {
                ItemDrop.ItemData item = items[i];
                if (!IsHarvestTool(item))
                {
                    continue;
                }

                if (item.m_shared.m_toolTier >= minToolTier &&
                    CompanionHarvestScanner.EffectiveDamage(item.m_shared.m_damages, modifiers) > 0f)
                {
                    return false;
                }
            }

            // "A melhor da lista que atenda": tier primeiro, dano efetivo como desempate.
            GameObject best = null;
            float bestRank = 0f;
            string[] names = ProvidedToolPrefabs.Split(ListSeparator);
            for (int i = 0; i < names.Length; i++)
            {
                string name = names[i].Trim();
                if (name.Length == 0)
                {
                    continue;
                }

                GameObject prefab = FindToolPrefab(name);
                ItemDrop drop = prefab != null ? prefab.GetComponent<ItemDrop>() : null;
                if (drop == null || drop.m_itemData == null || drop.m_itemData.m_shared == null)
                {
                    continue;
                }

                ItemDrop.ItemData candidate = drop.m_itemData;
                if (candidate.m_shared.m_toolTier < minToolTier)
                {
                    continue;
                }

                float damage = CompanionHarvestScanner.EffectiveDamage(
                    candidate.m_shared.m_damages, modifiers);
                if (damage <= 0f)
                {
                    continue;
                }

                float rank = candidate.m_shared.m_toolTier * 1000f + damage;
                if (rank > bestRank)
                {
                    bestRank = rank;
                    best = prefab;
                }
            }

            if (best == null)
            {
                if (Time.time - s_lastTierWarnAt >= 5f)
                {
                    s_lastTierWarnAt = Time.time;
                    Plugin.Log.LogWarning(
                        $"[ModIA] Nenhuma ferramenta de 'Coleta/FerramentasFornecidas' atende ao nó " +
                        $"de '{resourceName}' (tier mínimo {minToolTier}); inclua uma picareta/machado " +
                        "de tier maior na lista (ex.: PickaxeIron).");
                }

                return false;
            }

            // Já está no inventário: não cria de novo (evita duplicar a cada varredura).
            if (FindInInventory(inventory, best) != null)
            {
                return false;
            }

            if (AddItems(inventory, best, 1) == null)
            {
                return false;
            }

            Plugin.Log.LogInfo(
                $"[ModIA] Ferramenta de coleta fornecida pelo tier: " +
                $"'{Utils.GetPrefabName(best.name)}' (tier {best.GetComponent<ItemDrop>().m_itemData.m_shared.m_toolTier}, " +
                $"mínimo do nó {minToolTier}) para '{resourceName}'.");
            return true;
        }

        /// <summary>
        /// Procura o prefab de ferramenta sem poluir o log: nome inválido na config é avisado
        /// <b>uma vez</b> (a busca roda a cada varredura de coleta).
        /// </summary>
        private static GameObject FindToolPrefab(string name)        {
            ObjectDB database = ObjectDB.instance;
            GameObject prefab = database != null ? database.GetItemPrefab(name) : null;
            if (prefab != null)
            {
                return prefab;
            }

            if (database != null && s_missingToolWarned.Add(name))
            {
                Plugin.Log.LogWarning(
                    $"[ModIA] Ferramenta fornecida '{name}' não existe no ObjectDB; ignorada.");
            }

            return null;
        }

        /// <summary>
        /// Preenche <paramref name="buffer"/> com as ferramentas de golpe do companheiro (tier +
        /// dano por tipo). É o que a busca de nós usa para estimar golpes e saber o que ele
        /// <b>consegue</b> arranhar: tier abaixo do mínimo do nó é recusado pelo próprio jogo
        /// (<c>HitData.CheckToolTier</c>).
        /// </summary>
        internal static void GetHarvestToolProfiles(CompanionAI companion, List<HarvestToolProfile> buffer)
        {
            if (buffer == null)
            {
                return;
            }

            buffer.Clear();

            Humanoid humanoid = Resolve(companion);
            Inventory inventory = humanoid != null ? humanoid.GetInventory() : null;
            if (inventory == null)
            {
                return;
            }

            List<ItemDrop.ItemData> items = inventory.GetAllItems();
            for (int i = 0; i < items.Count; i++)
            {
                ItemDrop.ItemData item = items[i];
                if (!IsHarvestTool(item))
                {
                    continue;
                }

                HarvestToolProfile profile = default;
                profile.Tier = item.m_shared.m_toolTier;
                profile.Damage = item.m_shared.m_damages;
                buffer.Add(profile);
            }
        }

        /// <summary>
        /// Maior <c>m_toolTier</c> entre as ferramentas disponíveis no inventário do companheiro.
        /// O tier é o que o jogo cobra para arranhar o nó (<c>HitData.CheckToolTier</c>): sem ele o
        /// golpe não faz dano nenhum (o jogo mostra "duro demais").
        /// </summary>
        internal static int GetBestAvailableToolTier(CompanionAI companion)
        {
            GetHarvestToolProfiles(companion, s_toolBuffer);

            int best = 0;
            for (int i = 0; i < s_toolBuffer.Count; i++)
            {
                if (s_toolBuffer[i].Tier > best)
                {
                    best = s_toolBuffer[i].Tier;
                }
            }

            return best;
        }

        /// <summary>
        /// Equipa a melhor ferramenta do inventário para um nó com estas resistências
        /// (<paramref name="modifiers"/>): machado para madeira (dano de <i>chop</i>), picareta para
        /// pedra (<i>pickaxe</i>), e assim por diante — a mesma ideia do
        /// <c>GetBestHarvestingTool</c>/<c>CalculateWeaponEffectiveness</c> do Thrall, usando a
        /// tabela de modificadores do próprio jogo.
        ///
        /// <para>
        /// Ferramentas com tier abaixo do mínimo do nó levam penalidade forte na pontuação (só são
        /// escolhidas se não houver nada melhor), e armas sem golpe corpo-a-corpo (arcos, cajados)
        /// são descartadas — elas não quebram nó nenhum.
        /// </para>
        /// </summary>
        internal static bool TryEquipBestHarvestTool(
            CompanionAI companion,
            HitData.DamageModifiers modifiers,
            int minToolTier,
            string resourceName,
            out string chosenTool)
        {
            chosenTool = null;

            Humanoid humanoid = Resolve(companion);
            Inventory inventory = humanoid != null ? humanoid.GetInventory() : null;
            if (inventory == null)
            {
                return false;
            }

            ItemDrop.ItemData current = humanoid.GetCurrentWeapon();
            ItemDrop.ItemData best = null;
            float bestScore = -1f;

            List<ItemDrop.ItemData> items = inventory.GetAllItems();
            for (int i = 0; i < items.Count; i++)
            {
                ItemDrop.ItemData item = items[i];
                if (!IsHarvestTool(item))
                {
                    continue;
                }

                float score = CalculateToolEffectiveness(item, modifiers);
                if (score <= 0f)
                {
                    continue;
                }

                if (item.m_shared.m_toolTier < minToolTier)
                {
                    score *= 0.05f;
                }

                // Já equipada: leva um empurrãozinho para não ficar trocando de ferramenta à toa.
                if (item == current)
                {
                    score *= 1.1f;
                }

                if (score > bestScore)
                {
                    bestScore = score;
                    best = item;
                }
            }

            if (best == null)
            {
                Plugin.Log.LogWarning(
                    $"[ModIA] Companheiro não tem ferramenta de golpe para '{resourceName}'; " +
                    "a coleta não vai render (equipe um machado/picareta: modia_equip AxeStone).");
                return false;
            }

            if (best == current)
            {
                chosenTool = Utils.GetPrefabName(best.m_dropPrefab);
                return true;
            }

            if (best.m_dropPrefab == null)
            {
                return false;
            }

            chosenTool = Utils.GetPrefabName(best.m_dropPrefab);
            if (EquipWeapon(companion, chosenTool))
            {
                return true;
            }

            chosenTool = null;
            return false;
        }

        /// <summary>
        /// Ferramenta capaz de bater em recurso: equipável, com ataque primário corpo-a-corpo (sem
        /// projétil) e com dano. Cuidado: <c>IsWeapon()</c> do jogo devolve <c>false</c> para
        /// <c>ItemType.Tool</c> — é justamente onde moram as picaretas.
        /// </summary>
        internal static bool IsHarvestTool(ItemDrop.ItemData item)
        {
            if (item == null || item.m_shared == null || !item.IsEquipable() || !item.HavePrimaryAttack())
            {
                return false;
            }

            // Arcos/bestas e munição nunca quebram nó: a besta chega a listar dano de "chop", mas o
            // ataque é um projétil — sem esta guarda ela ganharia do machado e o companheiro ficaria
            // parado atirando na árvore.
            if (item.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Bow ||
                item.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Ammo ||
                !string.IsNullOrEmpty(item.m_shared.m_ammoType))
            {
                return false;
            }

            Attack attack = item.m_shared.m_attack;
            if (attack == null || attack.m_attackProjectile != null || attack.m_attackType == Attack.AttackType.Projectile)
            {
                return false;
            }

            HitData.DamageTypes damage = item.m_shared.m_damages;
            return damage.m_blunt > 0f || damage.m_slash > 0f || damage.m_pierce > 0f ||
                   damage.m_chop > 0f || damage.m_pickaxe > 0f;
        }

        /// <summary>
        /// Dano efetivo da ferramenta contra as resistências do nó. A tabela de
        /// <c>HitData.DamageModifiers</c> → multiplicador vive em
        /// <see cref="CompanionHarvestScanner.EffectiveDamage"/>, para a escolha da ferramenta e a
        /// pontuação do nó usarem exatamente a mesma conta.
        /// </summary>
        private static float CalculateToolEffectiveness(ItemDrop.ItemData tool, HitData.DamageModifiers modifiers)
        {
            return CompanionHarvestScanner.EffectiveDamage(tool.m_shared.m_damages, modifiers);
        }

        // ---------------------------------------------------------------------
        // Inspeção
        // ---------------------------------------------------------------------

        /// <summary>
        /// Fotografia legível do inventário e dos itens equipados (para o comando de console e
        /// para o log). Não usa LINQ e não aloca em loop de frame — só é chamado sob demanda.
        /// </summary>
        internal static List<string> GetInventorySnapshot(CompanionAI companion)
        {
            List<string> lines = new List<string>();

            Humanoid humanoid = Resolve(companion);
            if (humanoid == null)
            {
                lines.Add("[ModIA] Nenhum companheiro ativo no mundo.");
                return lines;
            }

            Inventory inventory = humanoid.GetInventory();
            if (inventory == null)
            {
                lines.Add("[ModIA] Companheiro sem inventário.");
                return lines;
            }

            string label = string.IsNullOrEmpty(humanoid.m_name) ? companion.gameObject.name : humanoid.m_name;
            List<ItemDrop.ItemData> items = inventory.GetAllItems();
            lines.Add($"[ModIA] Inventário de {label}: {items.Count} item(ns).");

            if (items.Count == 0)
            {
                lines.Add("[ModIA]   (vazio)");
            }

            for (int i = 0; i < items.Count; i++)
            {
                ItemDrop.ItemData item = items[i];
                if (item == null)
                {
                    continue;
                }

                string prefabName = item.m_dropPrefab != null
                    ? Utils.GetPrefabName(item.m_dropPrefab)
                    : item.m_shared.m_name;

                bool equipped = humanoid.IsItemEquiped(item);
                string slot = equipped ? " [equipado: " + DescribeEquippedSlot(humanoid, item) + "]" : string.Empty;

                lines.Add(
                    $"[ModIA]   {i}: {prefabName} x{item.m_stack} q{item.m_quality} " +
                    $"({item.m_shared.m_itemType}){slot}");
            }

            AppendVisualCapabilities(lines, humanoid.GetComponent<VisEquipment>());
            return lines;
        }

        // ---------------------------------------------------------------------
        // Internos
        // ---------------------------------------------------------------------

        private static Humanoid Resolve(CompanionAI companion)
        {
            if (companion == null)
            {
                Plugin.Log.LogWarning("[ModIA] Nenhum companheiro ativo (spawne com a tecla configurada).");
                return null;
            }

            Humanoid humanoid = companion.GetComponent<Humanoid>();
            if (humanoid == null)
            {
                Plugin.Log.LogError("[ModIA] Companheiro instanciado sem componente Humanoid.");
                return null;
            }

            ZNetView netView = companion.GetComponent<ZNetView>();
            if (netView != null && netView.IsValid() && !netView.IsOwner())
            {
                // Sem autoridade o ZDO não é escrito localmente: o equipamento não replica.
                Plugin.Log.LogWarning(
                    "[ModIA] Sem autoridade sobre o companheiro: o equipamento não será replicado no mundo.");
            }

            return humanoid;
        }

        private static GameObject FindItemPrefab(string prefabName)
        {
            if (string.IsNullOrEmpty(prefabName))
            {
                Plugin.Log.LogWarning("[ModIA] Nome de prefab vazio; equipamento cancelado.");
                return null;
            }

            ObjectDB database = ObjectDB.instance;
            if (database == null)
            {
                Plugin.Log.LogWarning("[ModIA] ObjectDB indisponível (fora do mundo?); equipamento cancelado.");
                return null;
            }

            string name = prefabName.Trim();
            GameObject prefab = database.GetItemPrefab(name);
            if (prefab == null || prefab.GetComponent<ItemDrop>() == null)
            {
                Plugin.Log.LogWarning(
                    $"[ModIA] Prefab de item '{name}' não existe em ObjectDB; item ignorado.");
                return null;
            }

            return prefab;
        }

        /// <summary>
        /// Clona o <see cref="ItemDrop.ItemData"/> do prefab e coloca no inventário devolvendo a
        /// instância exata que foi adicionada (é ela que o <c>EquipItem</c> exige).
        /// </summary>
        private static ItemDrop.ItemData AddItems(Inventory inventory, GameObject prefab, int amount)
        {
            // Mesmo valor que Inventory.AddItem(GameObject, int) usa.
            ItemDrop.ItemData item = CreateItem(prefab, amount, 1, 0f, Game.m_worldLevel, 0);
            if (item == null)
            {
                return null;
            }

            if (!inventory.AddItem(item))
            {
                return null;
            }

            // Para itens empilháveis o AddItem pode ter absorvido tudo em stacks existentes
            // (nesse caso a instância clonada não fica no inventário e não serve para equipar).
            return inventory.ContainsItem(item) ? item : null;
        }

        /// <summary>
        /// Cria um <see cref="ItemDrop.ItemData"/> a partir do prefab de item, com os valores dados
        /// (<c>0</c>/<c>≤ 0</c> = "use o padrão do item": durabilidade máxima, qualidade 1).
        /// Não adiciona nada no inventário — quem chama decide.
        /// </summary>
        private static ItemDrop.ItemData CreateItem(
            GameObject prefab,
            int stack,
            int quality,
            float durability,
            int worldLevel,
            int variant)
        {
            if (prefab == null)
            {
                return null;
            }

            ItemDrop drop = prefab.GetComponent<ItemDrop>();
            if (drop == null || drop.m_itemData == null || drop.m_itemData.m_shared == null)
            {
                return null;
            }

            ItemDrop.ItemData item = drop.m_itemData.Clone();
            item.m_dropPrefab = prefab;
            item.m_stack = Mathf.Clamp(stack, 1, Mathf.Max(1, item.m_shared.m_maxStackSize));
            item.m_quality = Mathf.Clamp(quality, 1, Mathf.Max(1, item.m_shared.m_maxQuality));
            item.m_variant = Mathf.Max(0, variant);
            item.m_worldLevel = Mathf.Max(0, worldLevel);

            // Item criado "do zero" pode vir com durabilidade zerada no template; sem isso o
            // Humanoid.EquipItem recusaria a arma/armadura (regra: durabilidade <= 0 não equipa).
            if (item.m_shared.m_useDurability)
            {
                float max = item.GetMaxDurability();
                item.m_durability = durability > 0f
                    ? Mathf.Clamp(durability, 0.01f, max)
                    : max;
            }

            return item;
        }

        private static ItemDrop.ItemData FindInInventory(Inventory inventory, GameObject prefab)
        {
            if (inventory == null || prefab == null)
            {
                return null;
            }

            return FindInInventoryByName(inventory, Utils.GetPrefabName(prefab));
        }

        private static ItemDrop.ItemData FindInInventory(Inventory inventory, string prefabName)
        {
            if (inventory == null || string.IsNullOrEmpty(prefabName))
            {
                return null;
            }

            return FindInInventoryByName(inventory, prefabName.Trim());
        }

        private static ItemDrop.ItemData FindInInventoryByName(Inventory inventory, string prefabName)
        {
            int wantedHash = prefabName.GetStableHashCode();
            List<ItemDrop.ItemData> items = inventory.GetAllItems();

            for (int i = 0; i < items.Count; i++)
            {
                ItemDrop.ItemData item = items[i];
                if (item == null || item.m_dropPrefab == null)
                {
                    continue;
                }

                if (Utils.GetPrefabName(item.m_dropPrefab).GetStableHashCode() == wantedHash)
                {
                    return item;
                }
            }

            return null;
        }

        /// <summary>
        /// Resolve o slot visual do item e chama a API hash-based do <see cref="VisEquipment"/>
        /// do Valheim 1.0. Para armas a mão é a que o <see cref="Humanoid"/> escolheu
        /// (<c>RightItem</c>/<c>LeftItem</c>); armaduras saem do <c>ItemType</c>.
        /// </summary>
        private static bool SyncVisual(Humanoid humanoid, ItemDrop.ItemData item, VisSlot? forcedSlot)
        {
            VisEquipment visEq = humanoid.GetComponent<VisEquipment>();
            if (visEq == null)
            {
                Plugin.Log.LogWarning(
                    "[ModIA] Companheiro sem VisEquipment: item equipado na lógica, mas sem visual.");
                return false;
            }

            if (item.m_dropPrefab == null)
            {
                Plugin.Log.LogWarning(
                    "[ModIA] Item sem m_dropPrefab; não é possível calcular o hash visual.");
                return false;
            }

            VisSlot slot = ResolveSlot(humanoid, item, forcedSlot);
            ApplySlot(visEq, item, slot);
            InspectVisualsOnce(visEq);
            return true;
        }

        private static VisSlot ResolveSlot(Humanoid humanoid, ItemDrop.ItemData item, VisSlot? forcedSlot)
        {
            // Mão primeiro: quem decide mão direita/esquerda é o EquipItem do jogo (por ItemType),
            // então o slot visual precisa seguir o Humanoid — senão a arma apareceria nas duas mãos.
            if (humanoid.RightItem == item)
            {
                return VisSlot.HandRight;
            }

            if (humanoid.LeftItem == item)
            {
                return VisSlot.HandLeft;
            }

            if (forcedSlot.HasValue)
            {
                return forcedSlot.Value;
            }

            VisSlot? byType = SlotForItemType(item.m_shared.m_itemType);
            return byType ?? VisSlot.HandRight;
        }

        private static VisSlot? SlotForItemType(ItemDrop.ItemData.ItemType type)
        {
            switch (type)
            {
                case ItemDrop.ItemData.ItemType.Chest:
                    return VisSlot.Chest;
                case ItemDrop.ItemData.ItemType.Legs:
                    return VisSlot.Legs;
                case ItemDrop.ItemData.ItemType.Helmet:
                    return VisSlot.Helmet;
                case ItemDrop.ItemData.ItemType.Shoulder:
                    return VisSlot.Shoulder;
                case ItemDrop.ItemData.ItemType.Utility:
                    return VisSlot.Utility;
                default:
                    return null;
            }
        }

        /// <summary>
        /// Assinaturas do <see cref="VisEquipment"/> no Valheim 1.0.17 (confirmadas no
        /// <c>assembly_valheim.dll</c>): todas recebem <c>int itemHash</c> e nunca string.
        /// O hash é o mesmo usado por <c>Humanoid.SetupVisEquipment</c>, o que garante que a
        /// aparência renderizada bate com o item equipado no <see cref="Humanoid"/>.
        /// </summary>
        private static void ApplySlot(VisEquipment visEq, ItemDrop.ItemData item, VisSlot slot)
        {
            int itemHash = Utils.GetPrefabName(item.m_dropPrefab).GetStableHashCode();
            int variant = item.m_variant;
            int quality = item.m_quality;

            switch (slot)
            {
                case VisSlot.HandRight:
                    visEq.SetRightItem(itemHash, quality);
                    break;
                case VisSlot.HandLeft:
                    visEq.SetLeftItem(itemHash, variant, quality);
                    break;
                case VisSlot.BackLeft:
                    visEq.SetLeftBackItem(itemHash, variant, quality);
                    break;
                case VisSlot.BackRight:
                    visEq.SetRightBackItem(itemHash, quality);
                    break;
                case VisSlot.Chest:
                    visEq.SetChestItem(itemHash);
                    break;
                case VisSlot.Legs:
                    visEq.SetLegItem(itemHash);
                    break;
                case VisSlot.Helmet:
                    visEq.SetHelmetItem(itemHash);
                    break;
                case VisSlot.Shoulder:
                    visEq.SetShoulderItem(itemHash, variant, quality);
                    break;
                case VisSlot.Utility:
                    visEq.SetUtilityItem(itemHash);
                    break;
                default:
                    return;
            }

            Plugin.Log.LogInfo(
                $"[ModIA] VisEquipment.{slot} <- '{Utils.GetPrefabName(item.m_dropPrefab)}' " +
                $"(hash {itemHash}, variant {variant}, quality {quality}).");
        }

        private static string DescribeEquippedSlot(Humanoid humanoid, ItemDrop.ItemData item)
        {
            if (humanoid.RightItem == item)
            {
                return "mão direita";
            }

            if (humanoid.LeftItem == item)
            {
                return "mão esquerda";
            }

            VisSlot? byType = SlotForItemType(item.m_shared.m_itemType);
            return byType.HasValue ? byType.Value.ToString() : "outro";
        }

        /// <summary>
        /// Diagnóstico único por sessão do que o <see cref="VisEquipment"/> do prefab consegue
        /// renderizar. Peito/pernas são pintados no <c>m_bodyModel</c> (o corpo do viking do
        /// prefab 'Player', ver <see cref="CompanionPrefab"/>); elmo/capa/armas se penduram nos
        /// ossos do esqueleto. Se o corpo base não tiver <c>m_bodyModel</c>, a armadura de
        /// peito/pernas é equipada na lógica mas não aparece.
        /// </summary>
        private static void InspectVisualsOnce(VisEquipment visEq)
        {
            if (_visualsInspected)
            {
                return;
            }

            _visualsInspected = true;
            AppendVisualCapabilities(null, visEq);
        }

        private static void AppendVisualCapabilities(List<string> lines, VisEquipment visEq)
        {
            string[] report;

            if (visEq == null)
            {
                report = new[] { "[ModIA] VisEquipment: AUSENTE." };
            }
            else
            {
                report = new[]
                {
                    $"[ModIA] VisEquipment: body model = {(visEq.m_bodyModel != null ? "ok" : "ausente")}, " +
                    $"modelos de corpo = {visEq.m_models.Length}, " +
                    $"mão esquerda = {(visEq.m_leftHand != null ? "ok" : "ausente")}, " +
                    $"mão direita = {(visEq.m_rightHand != null ? "ok" : "ausente")}.",
                    "[ModIA] Peito/pernas só aparecem se o corpo base tiver 'body model' " +
                    "(o corpo do viking do prefab 'Player' tem); elmo/capa/armas penduram nos ossos."
                };
            }

            for (int i = 0; i < report.Length; i++)
            {
                lines?.Add(report[i]);
                Plugin.Log.LogInfo(report[i]);
            }
        }
    }
}

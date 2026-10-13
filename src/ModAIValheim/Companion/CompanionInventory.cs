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

        /// <summary>Solta o estado estático (unload do plugin / fim do mundo).</summary>
        internal static void Shutdown()
        {
            _visualsInspected = false;
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
            ItemDrop drop = prefab.GetComponent<ItemDrop>();
            if (drop == null)
            {
                return null;
            }

            int maxStack = Mathf.Max(1, drop.m_itemData.m_shared.m_maxStackSize);

            ItemDrop.ItemData item = drop.m_itemData.Clone();
            item.m_dropPrefab = prefab;
            item.m_stack = Mathf.Clamp(amount, 1, maxStack);
            // Mesmo valor que Inventory.AddItem(GameObject, int) usa.
            item.m_worldLevel = (byte)Game.m_worldLevel;

            // Item criado "do zero" pode vir com durabilidade zerada no template; sem isso o
            // Humanoid.EquipItem recusaria a arma/armadura (regra: durabilidade <= 0 não equipa).
            if (item.m_shared.m_useDurability && item.m_durability <= 0f)
            {
                item.m_durability = item.GetMaxDurability();
            }

            if (!inventory.AddItem(item))
            {
                return null;
            }

            // Para itens empilháveis o AddItem pode ter absorvido tudo em stacks existentes
            // (nesse caso a instância clonada não fica no inventário e não serve para equipar).
            return inventory.ContainsItem(item) ? item : null;
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

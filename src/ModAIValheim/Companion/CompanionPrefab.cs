using System;
using Jotunn.Managers;
using UnityEngine;

namespace ModAIValheim.Companion
{
    /// <summary>
    /// Registro do prefab do companheiro.
    ///
    /// O prefab é um clone de um humanoid vanilla (ver <see cref="BasePrefabName"/>) ao qual
    /// removemos a IA vanilla e adicionamos a nossa <see cref="CompanionAI"/>. Nada de asset
    /// bundle próprio: nesta fase reaproveitamos o modelo/skeleton/rede do prefab do jogo.
    ///
    /// O registro tem de acontecer na fase em que os prefabs vanilla já estão em memória mas
    /// ainda antes do <c>ZNetScene.Awake</c> — é o evento
    /// <see cref="PrefabManager.OnVanillaPrefabsAvailable"/> que o Jotunn expõe para isso
    /// (o Jotunn então injeta o prefab no <c>ZNetScene</c> no <c>Awake</c> dele).
    /// </summary>
    internal static class CompanionPrefab
    {
        /// <summary>Nome do nosso prefab (não pode colidir com nada do jogo/Jotunn).</summary>
        internal const string PrefabName = "ModIA_Companion";

        /// <summary>Prefab vanilla clonado como base.</summary>
        internal const string BasePrefabName = "Draugr";

        private static GameObject _prefab;
        private static bool _hooked;

        /// <summary>Prefab registrado (null enquanto não estiver pronto).</summary>
        internal static GameObject Prefab => _prefab;

        internal static void Hook()
        {
            if (_hooked)
            {
                return;
            }

            _hooked = true;
            PrefabManager.OnVanillaPrefabsAvailable += CreateCompanionPrefab;
        }

        internal static void Unhook()
        {
            if (!_hooked)
            {
                return;
            }

            _hooked = false;
            PrefabManager.OnVanillaPrefabsAvailable -= CreateCompanionPrefab;
        }

        /// <summary>
        /// Devolve o prefab pronto para instanciar, procurando primeiro no <c>ZNetScene</c>
        /// (caminho normal durante o jogo) e caindo para o <c>PrefabManager</c>.
        /// </summary>
        internal static GameObject GetPrefab()
        {
            if (_prefab != null)
            {
                return _prefab;
            }

            ZNetScene scene = ZNetScene.instance;
            if (scene != null)
            {
                GameObject fromScene = scene.GetPrefab(PrefabName);
                if (fromScene != null)
                {
                    return fromScene;
                }
            }

            return PrefabManager.Instance.GetPrefab(PrefabName);
        }

        /// <summary>
        /// Handler do evento do Jotunn. Roda a cada carregamento de ObjectDB (menu/mundo),
        /// então precisa ser idempotente: o registro é feito uma única vez por sessão.
        /// </summary>
        private static void CreateCompanionPrefab()
        {
            if (_prefab != null)
            {
                return;
            }

            GameObject clone = CloneBasePrefab();
            if (clone == null)
            {
                Plugin.Log.LogError(
                    $"[ModIA] Não foi possível clonar o prefab vanilla '{BasePrefabName}' " +
                    "para criar o companheiro.");
                return;
            }

            StripVanillaBehaviour(clone);

            clone.AddComponent<CompanionAI>();

            // Nesta fase o companheiro não é persistente: sem ZDO salvo no mundo
            // (não mexe no save) e sem duplicatas órfãs depois de recarregar a área.
            ZNetView netView = clone.GetComponent<ZNetView>();
            if (netView != null)
            {
                netView.m_persistent = false;
            }

            ValidatePrefab(clone);

            PrefabManager.Instance.AddPrefab(clone);
            _prefab = clone;
            Plugin.Log.LogInfo(
                $"[ModIA] Prefab '{PrefabName}' registrado (clone de '{BasePrefabName}').");
        }

        /// <summary>
        /// Confere os componentes que o companheiro precisa. Se algo faltar, o log diz o que é
        /// (melhor descobrir na carga do mundo do que com o NPC invisível/parado em jogo).
        /// </summary>
        private static void ValidatePrefab(GameObject prefab)
        {
            CheckComponent<Character>(prefab, false);
            CheckComponent<BaseAI>(prefab, false);
            CheckComponent<ZNetView>(prefab, false);
            CheckComponent<ZSyncAnimation>(prefab, false);
            CheckComponent<CapsuleCollider>(prefab, false);
            CheckComponent<Rigidbody>(prefab, false);
            CheckComponent<VisEquipment>(prefab, false);
            CheckComponent<Animator>(prefab, true);
            CheckComponent<CharacterAnimEvent>(prefab, true);
        }

        private static void CheckComponent<T>(GameObject prefab, bool inChildren) where T : Component
        {
            T component = inChildren ? prefab.GetComponentInChildren<T>() : prefab.GetComponent<T>();
            if (component == null)
            {
                Plugin.Log.LogError(
                    $"[ModIA] Prefab '{prefab.name}' sem componente obrigatório '{typeof(T).Name}'.");
            }
        }

        private static GameObject CloneBasePrefab()
        {
            // Mesmo caminho que o CreatureManager do Jotunn usa para achar criaturas vanilla
            // pelo sistema de SoftReferenceableAssets do Valheim 1.0.
            Character vanilla = PrefabManager.Cache.GetPrefab<Character>(BasePrefabName);
            if (vanilla != null)
            {
                return PrefabManager.Instance.CreateClonedPrefab(PrefabName, vanilla.gameObject);
            }

            Plugin.Log.LogWarning(
                $"[ModIA] '{BasePrefabName}' não encontrado no cache de criaturas; " +
                "tentando clonar pelo nome.");

            return PrefabManager.Instance.CreateClonedPrefab(PrefabName, BasePrefabName);
        }

        /// <summary>
        /// Remove o comportamento de criatura selvagem do clone.
        /// </summary>
        private static void StripVanillaBehaviour(GameObject prefab)
        {
            // Qualquer IA vanilla precisa sair: além de disputar o controle do movimento, ela
            // registraria os mesmos RPCs no ZNetView ("Alert", "RPC_Wakeup", ...) e o registro
            // duplicado lançaria exceção.
            BaseAI[] vanillaAis = prefab.GetComponents<BaseAI>();
            for (int i = 0; i < vanillaAis.Length; i++)
            {
                Plugin.Log.LogInfo(
                    $"[ModIA] Removendo IA vanilla '{vanillaAis[i].GetType().Name}' do prefab.");
                UnityEngine.Object.DestroyImmediate(vanillaAis[i]);
            }

            // Sem loot ao morrer e sem o sistema de domesticação vanilla.
            DestroyIfPresent<CharacterDrop>(prefab);
            DestroyIfPresent<Tameable>(prefab);

            // Facção já no prefab: assim todo cliente com o mod vê o companheiro como aliado
            // (m_faction não é sincronizada pelo ZDO).
            Character character = prefab.GetComponent<Character>();
            if (character != null)
            {
                character.m_faction = Character.Faction.Players;
            }

            ClearVanillaDefaultItems(prefab);
        }

        /// <summary>
        /// Zera o equipamento que o <c>Humanoid</c> do clone distribuiria sozinho
        /// (<c>GiveDefaultItems</c>, chamado no <c>Start</c> de NPCs).
        ///
        /// O companheiro nasce "pelado" de propósito: quem define a aparência é o nosso loadout
        /// (<see cref="CompanionInventory"/>), e assim não há gear aleatório do Draugr no
        /// inventário nem no <c>VisEquipment</c>. Os arrays viram vazios (nunca null) porque o
        /// código vanilla itera e lê <c>.Length</c> sem checar null.
        /// </summary>
        private static void ClearVanillaDefaultItems(GameObject prefab)
        {
            Humanoid humanoid = prefab.GetComponent<Humanoid>();
            if (humanoid == null)
            {
                return;
            }

            humanoid.m_defaultItems = Array.Empty<GameObject>();
            humanoid.m_randomWeapon = Array.Empty<GameObject>();
            humanoid.m_randomArmor = Array.Empty<GameObject>();
            humanoid.m_randomShield = Array.Empty<GameObject>();
            humanoid.m_randomSets = Array.Empty<Humanoid.ItemSet>();
            humanoid.m_randomItems = Array.Empty<Humanoid.RandomItem>();
        }

        private static void DestroyIfPresent<T>(GameObject prefab) where T : Component
        {
            T component = prefab.GetComponent<T>();
            if (component != null)
            {
                UnityEngine.Object.DestroyImmediate(component);
            }
        }
    }
}

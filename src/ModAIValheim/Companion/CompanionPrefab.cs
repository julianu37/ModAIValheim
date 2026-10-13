using System;
using System.Collections.Generic;
using System.Reflection;
using Jotunn.Managers;
using UnityEngine;

namespace ModAIValheim.Companion
{
    /// <summary>
    /// Registro do prefab do companheiro (Fase 2.3b: corpo de viking).
    ///
    /// <para>
    /// O prefab é um clone do <b>corpo do jogador</b> (<c>'Player'</c>) ao qual trocamos o
    /// componente <see cref="Player"/> por um <see cref="Humanoid"/> equivalente, removemos o que
    /// só existe no jogador local (input/GUI) e adicionamos a nossa <see cref="CompanionAI"/>.
    /// Isso é o que faz a <b>armadura de peito/pernas renderizar</b>: o corpo do jogador tem
    /// <c>VisEquipment.m_bodyModel</c> (o <c>SkinnedMeshRenderer</c> do viking) e é nele que
    /// <c>VisEquipment.SetChestEquipped/SetLegEquipped</c> pintam as texturas da armadura
    /// (ver <c>docs/re</c> e o decompilado do 1.0.17).
    /// </para>
    ///
    /// <para>
    /// <b>Por que trocar o componente em vez de só apagar o <c>Player</c>:</b> no prefab do
    /// jogador <c>Player : Humanoid : Character</c> é <b>um único componente</b> (não existe um
    /// <c>Character</c>/<c>Humanoid</c> separado como nas criaturas). Apagar o <c>Player</c>
    /// deixaria o clone sem <c>Character</c> nenhum — sem HP, sem movimento, sem inventário.
    /// Por isso copiamos os campos serializados de <c>Character</c>/<c>Humanoid</c> do próprio
    /// componente <c>Player</c> (dados do prefab: vida, velocidades, efeitos, resistências) para
    /// um <see cref="Humanoid"/> novo.
    /// </para>
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

        /// <summary>
        /// Prefab vanilla clonado como base: o corpo do jogador (viking). É o único prefab
        /// vanilla com <c>VisEquipment.m_bodyModel</c> — logo o único em que a armadura de
        /// peito/pernas aparece.
        /// </summary>
        internal const string BasePrefabName = "Player";

        /// <summary>Campos serializados que descrevem o corpo (declarados fora do <c>Player</c>).</summary>
        private static readonly Type[] s_bodyDataTypes = { typeof(Humanoid), typeof(Character) };

        private const BindingFlags BodyDataFlags =
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

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

            GameObject source = ResolveBasePrefab();
            if (source == null)
            {
                Plugin.Log.LogError(
                    $"[ModIA] Não foi possível resolver o prefab base '{BasePrefabName}' " +
                    "para criar o companheiro.");
                return;
            }

            GameObject clone = PrefabManager.Instance.CreateClonedPrefab(PrefabName, source);
            if (clone == null)
            {
                Plugin.Log.LogError(
                    $"[ModIA] Não foi possível clonar o prefab vanilla '{BasePrefabName}' " +
                    "para criar o companheiro.");
                return;
            }

            ConvertPlayerBodyToNpc(clone);

            // Sem Character/Humanoid o NPC nem se move: melhor não registrar nada e dizer no log
            // do que spawnar um companheiro quebrado.
            if (clone.GetComponent<Character>() == null || clone.GetComponent<Humanoid>() == null)
            {
                Plugin.Log.LogError(
                    "[ModIA] O clone ficou sem Character/Humanoid (conversão do corpo falhou); " +
                    "companheiro não foi registrado.");
                return;
            }

            StripVanillaBehaviour(clone);
            ConfigureVisEquipment(clone);

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
                $"[ModIA] Prefab '{PrefabName}' registrado (clone de '{BasePrefabName}', corpo de viking).");
        }

        // ---------------------------------------------------------------------
        // Base: corpo do jogador
        // ---------------------------------------------------------------------

        /// <summary>
        /// Acha o asset do prefab <c>'Player'</c>. A ordem importa: as duas primeiras fontes
        /// devolvem sempre o <b>asset</b> (nunca a instância viva do jogador local, que teria
        /// estado de runtime e não serve para clonar).
        /// </summary>
        private static GameObject ResolveBasePrefab()
        {
            // 1) Referência serializada do próprio jogo (bootstrap scene): é o asset do prefab.
            Game game = Game.instance;
            if (game != null && game.m_playerPrefab != null)
            {
                Plugin.Log.LogInfo($"[ModIA] Base '{BasePrefabName}' obtida de Game.m_playerPrefab.");
                return game.m_playerPrefab;
            }

            // 2) ZNetScene (dentro do mundo) — m_prefabs guarda os assets dos prefabs de rede.
            ZNetScene scene = ZNetScene.instance;
            if (scene != null)
            {
                GameObject fromScene = scene.GetPrefab(BasePrefabName);
                if (fromScene != null)
                {
                    Plugin.Log.LogInfo($"[ModIA] Base '{BasePrefabName}' obtida do ZNetScene.");
                    return fromScene;
                }
            }

            // 3) Prefabs/cache do Jotunn (caminho de menu, quando não há ZNetScene).
            GameObject fromJotunn = PrefabManager.Instance.GetPrefab(BasePrefabName);
            if (IsAsset(fromJotunn))
            {
                Plugin.Log.LogInfo($"[ModIA] Base '{BasePrefabName}' obtida do PrefabManager.");
                return fromJotunn;
            }

            Character cached = PrefabManager.Cache.GetPrefab<Character>(BasePrefabName);
            if (cached != null && IsAsset(cached.gameObject))
            {
                Plugin.Log.LogInfo($"[ModIA] Base '{BasePrefabName}' obtida do cache de Character.");
                return cached.gameObject;
            }

            return null;
        }

        /// <summary>
        /// Um asset de prefab (vindo de asset bundle) não pertence a nenhuma cena carregada;
        /// uma instância viva pertence. Serve para recusar clonar o jogador em cena.
        /// </summary>
        private static bool IsAsset(GameObject candidate)
        {
            return candidate != null && !candidate.scene.IsValid();
        }

        /// <summary>
        /// Converte o clone do prefab do jogador em NPC:
        /// sai o componente <see cref="Player"/> (input/GUI/lista de jogadores) e entra um
        /// <see cref="Humanoid"/> com os dados do corpo do jogador; saem também os componentes
        /// que só fazem sentido no jogador local.
        /// </summary>
        private static void ConvertPlayerBodyToNpc(GameObject prefab)
        {
            Player playerComponent = prefab.GetComponent<Player>();
            if (playerComponent == null)
            {
                Plugin.Log.LogError(
                    $"[ModIA] O clone de '{BasePrefabName}' não tem o componente 'Player'; " +
                    "o companheiro ficaria sem Character/Humanoid (base do prefab mudou?). " +
                    "Se a base já for uma criatura vanilla, ela tem Humanoid próprio.");
                return;
            }

            // 1) Fotografa os campos serializados de Character/Humanoid ANTES de destruir: o
            //    componente Player é a única fonte dos dados do corpo (vida, velocidades,
            //    efeitos, resistências, mods de dano). Sem isso o Humanoid novo usaria os
            //    defaults do código.
            List<KeyValuePair<FieldInfo, object>> bodyData = SnapshotBodyData(playerComponent);

            // 2) Player sai. Além de tratar input/GUI, ele se registra em Player.s_players e o
            //    FixedUpdate dele DESTRÓI o GameObject quando o objeto não é o jogador local
            //    ("Destroying old local player") — ou seja, o companheiro sumiria no 1º frame.
            UnityEngine.Object.DestroyImmediate(playerComponent);

            // 3) Componentes estritamente do jogador local:
            //    PlayerController lê ZInput e escreve direto no Player (null → NRE por FixedUpdate);
            //    Skills guarda uma referência ao Player (que deixou de existir).
            DestroyIfPresent<PlayerController>(prefab);
            DestroyIfPresent<Skills>(prefab);

            // 4) Humanoid novo + dados do corpo restaurados. O Humanoid traz Character junto
            //    (herança) e cria o próprio Inventory em campo readonly.
            Humanoid humanoid = prefab.AddComponent<Humanoid>();
            int restored = RestoreBodyData(bodyData, humanoid);

            Plugin.Log.LogInfo(
                $"[ModIA] Corpo de '{BasePrefabName}' convertido para Humanoid " +
                $"({restored} campos de Character/Humanoid copiados, vida base {humanoid.m_health}).");
        }

        /// <summary>
        /// Copia os campos de instância declarados em <see cref="Character"/> e
        /// <see cref="Humanoid"/> (o clone do Player não sofreu <c>Awake</c>, então os campos de
        /// runtime ainda estão zerados).
        /// </summary>
        private static List<KeyValuePair<FieldInfo, object>> SnapshotBodyData(Component source)
        {
            List<KeyValuePair<FieldInfo, object>> values = new List<KeyValuePair<FieldInfo, object>>();

            for (int t = 0; t < s_bodyDataTypes.Length; t++)
            {
                FieldInfo[] fields = s_bodyDataTypes[t].GetFields(BodyDataFlags);
                for (int i = 0; i < fields.Length; i++)
                {
                    FieldInfo field = fields[i];

                    // readonly (ex.: Humanoid.m_inventory) fica com a instância nova do componente.
                    if (field.IsInitOnly || field.IsLiteral)
                    {
                        continue;
                    }

                    values.Add(new KeyValuePair<FieldInfo, object>(field, field.GetValue(source)));
                }
            }

            return values;
        }

        private static int RestoreBodyData(List<KeyValuePair<FieldInfo, object>> bodyData, Humanoid target)
        {
            int restored = 0;

            for (int i = 0; i < bodyData.Count; i++)
            {
                FieldInfo field = bodyData[i].Key;
                try
                {
                    field.SetValue(target, bodyData[i].Value);
                    restored++;
                }
                catch (Exception e)
                {
                    Plugin.Log.LogWarning(
                        $"[ModIA] Campo '{field.DeclaringType?.Name}.{field.Name}' não pôde ser " +
                        $"copiado para o Humanoid: {e.Message}");
                }
            }

            return restored;
        }

        /// <summary>
        /// O <see cref="VisEquipment"/> do corpo do jogador é quem renderiza peito/pernas
        /// (pintando <c>m_bodyModel</c>) e cabelo/barba/costume (só quando <c>m_isPlayer</c>).
        /// </summary>
        private static void ConfigureVisEquipment(GameObject prefab)
        {
            VisEquipment visEq = prefab.GetComponent<VisEquipment>();
            if (visEq == null)
            {
                Plugin.Log.LogError(
                    $"[ModIA] Clone de '{BasePrefabName}' sem VisEquipment; armadura não teria visual.");
                return;
            }

            if (!visEq.m_isPlayer)
            {
                Plugin.Log.LogWarning(
                    "[ModIA] VisEquipment do clone veio com m_isPlayer = false; forçando true para " +
                    "renderizar corpo/cabelo/barba (o corpo base é o do jogador).");
                visEq.m_isPlayer = true;
            }

            visEq.m_isArmorStand = false;

            // Referência serializada ao componente Player que acabamos de remover.
            visEq.m_playerComponent = null;

            if (visEq.m_bodyModel == null)
            {
                Plugin.Log.LogError(
                    "[ModIA] Clone sem VisEquipment.m_bodyModel: armadura de peito/pernas " +
                    "não vai renderizar.");
                return;
            }

            Plugin.Log.LogInfo(
                $"[ModIA] Corpo base ok: m_bodyModel = '{visEq.m_bodyModel.name}', " +
                $"modelos de corpo = {visEq.m_models.Length}, m_isPlayer = {visEq.m_isPlayer}.");
        }

        // ---------------------------------------------------------------------
        // Ajustes de NPC
        // ---------------------------------------------------------------------

        /// <summary>
        /// Confere os componentes que o companheiro precisa. Se algo faltar, o log diz o que é
        /// (melhor descobrir na carga do mundo do que com o NPC invisível/parado em jogo).
        /// </summary>
        private static void ValidatePrefab(GameObject prefab)
        {
            CheckComponent<Character>(prefab, false);
            CheckComponent<Humanoid>(prefab, false);
            CheckComponent<BaseAI>(prefab, false);
            CheckComponent<ZNetView>(prefab, false);
            CheckComponent<ZSyncAnimation>(prefab, false);
            CheckComponent<ZSyncTransform>(prefab, false);
            CheckComponent<CapsuleCollider>(prefab, false);
            CheckComponent<Rigidbody>(prefab, false);
            CheckComponent<VisEquipment>(prefab, false);
            CheckComponent<Animator>(prefab, true);
            CheckComponent<CharacterAnimEvent>(prefab, true);
            CheckComponent<Player>(prefab, false, required: false);
        }

        private static void CheckComponent<T>(GameObject prefab, bool inChildren, bool required = true)
            where T : Component
        {
            T component = inChildren ? prefab.GetComponentInChildren<T>() : prefab.GetComponent<T>();
            if (component != null)
            {
                return;
            }

            if (required)
            {
                Plugin.Log.LogError(
                    $"[ModIA] Prefab '{prefab.name}' sem componente obrigatório '{typeof(T).Name}'.");
            }
            else
            {
                Plugin.Log.LogInfo(
                    $"[ModIA] Prefab '{prefab.name}' sem '{typeof(T).Name}' (esperado: é um NPC).");
            }
        }

        /// <summary>
        /// Remove o comportamento de criatura selvagem do clone e aplica a identidade de NPC
        /// aliado. No prefab do jogador não há IA vanilla, mas a remoção fica aqui para o caso de
        /// a base voltar a ser uma criatura.
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
        /// (<c>GiveDefaultItems</c>, chamado no <c>Start</c> de NPCs — repare que o
        /// <c>Start</c> do Humanoid itera os arrays sem checar null, então eles nunca podem
        /// voltar a ser null).
        ///
        /// O companheiro nasce "pelado" de propósito: quem define a aparência é o nosso loadout
        /// (<see cref="CompanionInventory"/>) e a aparência padrão
        /// (<see cref="CompanionAppearance"/>).
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

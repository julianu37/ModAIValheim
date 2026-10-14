using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using UnityEngine;

namespace ModAIValheim.Companion
{
    /// <summary>
    /// Tarefa de **coleta de recursos** do companheiro (Fase 2.4b).
    ///
    /// <para>
    /// Espelha a <i>lógica</i> do <c>HarvestAction</c>/<c>Harvesting_Start</c>/<c>ProcessHarvestCommand</c>
    /// do Thrall (ver <c>docs/re/thrall-features.md</c>, seção HARVESTING), sem copiar código:
    /// </para>
    /// <list type="number">
    ///   <item><description>o jogador diz <b>qual recurso</b> quer (nome do item que cai do nó —
    ///   <c>Wood</c>, <c>Stone</c>, <c>Raspberry</c>…), opcionalmente com uma quantidade;</description></item>
    ///   <item><description>o companheiro procura nós no raio configurado e escolhe o de maior
    ///   <b>valor esperado</b> (<see cref="CompanionHarvestScanner"/>), pontuando o rendimento do
    ///   recurso pedido, o esforço (golpes) e a distância — ver
    ///   <c>CompanionHarvestScanner.CalculateScore</c>;</description></item>
    ///   <item><description>equipa a ferramenta adequada, anda até o nó, bate nele até destruí-lo,
    ///   pega os drops e passa para o próximo — até atingir a quantidade pedida ou acabar o raio.</description></item>
    /// </list>
    ///
    /// <para>
    /// Este tipo guarda <b>estado e política</b> (o que coletar, quanto falta, qual nó, quando
    /// desistir). O comportamento por frame (andar/mirar/bater/pegar) fica na <see cref="CompanionAI"/>,
    /// que é quem roda no dono do ZDO (modelo server-authoritative).
    /// </para>
    /// </summary>
    internal static class CompanionHarvest
    {
        /// <summary>Segundos sem progresso antes de largar o nó atual e procurar outro.</summary>
        internal const float NodeGiveUpSeconds = 7f;

        /// <summary>Intervalo mínimo (s) entre tentativas de golpe em um nó.</summary>
        internal const float StrikeInterval = 0.25f;

        /// <summary>Distância (m) em que paramos para colher um <c>Pickable</c> ou pegar um drop.</summary>
        internal const float InteractReach = 2.2f;

        /// <summary>Duração (s) do veto a um nó que se mostrou impossível/travado.</summary>
        internal const float BlacklistSeconds = 90f;

        /// <summary>Camadas padrão varridas pela busca de nós (config <c>Camadas</c>).</summary>
        internal const string DefaultLayerNames = "Default,static_solid,Default_small,piece,piece_nonsolid,item";

        // ---------------------------------------------------------------------
        // Configuração
        // ---------------------------------------------------------------------

        private static ConfigEntry<KeyCode> _harvestKey;
        private static ConfigEntry<string> _defaultResource;
        private static ConfigEntry<int> _defaultAmount;
        private static ConfigEntry<float> _scanRadius;
        private static ConfigEntry<float> _scanInterval;
        private static ConfigEntry<bool> _pauseOnCombat;
        private static ConfigEntry<bool> _collectDrops;
        private static ConfigEntry<string> _layerNames;

        internal static KeyCode HarvestKey => _harvestKey != null ? _harvestKey.Value : KeyCode.F4;

        internal static string DefaultResource =>
            _defaultResource != null && !string.IsNullOrEmpty(_defaultResource.Value)
                ? _defaultResource.Value.Trim()
                : "Wood";

        internal static int DefaultAmount => _defaultAmount != null ? Mathf.Max(0, _defaultAmount.Value) : 0;

        internal static float ScanRadius => _scanRadius != null ? Mathf.Clamp(_scanRadius.Value, 5f, 150f) : 40f;

        internal static float ScanInterval => _scanInterval != null ? Mathf.Clamp(_scanInterval.Value, 0.2f, 3f) : 0.5f;

        /// <summary>
        /// Distância máxima (m) entre o nó em trabalho e o alvo que o companheiro segue (o jogador).
        /// Passando disso o nó é largado para ele não sair vagando atrás de recursos: coleta é
        /// serviço, não fuga.
        /// </summary>
        internal static float MaxNodeDistanceFromFollowTarget => ScanRadius + 15f;

        /// <summary>
        /// Em Defensive/Aggressive o combate tem <b>prioridade</b>: a coleta é pausada enquanto o
        /// companheiro tem um alvo hostil (ver a regra em <c>CompanionAI</c>). No Passive ele
        /// ignora inimigos e continua coletando.
        /// </summary>
        internal static bool PauseOnCombat => _pauseOnCombat == null || _pauseOnCombat.Value;

        /// <summary>Se os drops no chão devem ser catados (é o que conta a quantidade coletada).</summary>
        internal static bool CollectDrops => _collectDrops == null || _collectDrops.Value;

        internal static string LayerNames =>
            _layerNames != null && !string.IsNullOrEmpty(_layerNames.Value)
                ? _layerNames.Value
                : DefaultLayerNames;

        // ---------------------------------------------------------------------
        // Estado da tarefa (o companheiro é único, como no resto do mod)
        // ---------------------------------------------------------------------

        private static bool _active;
        private static bool _hasLimit;
        private static string _resource = string.Empty;
        private static int _remaining;
        private static int _collected;
        private static float _startedAt;
        private static GameObject _node;
        private static bool _pausedForCombat;

        /// <summary>Progresso do nó atual (revisão do ZDO e distância), para detectar travamento.</summary>
        private static float _nodeLastProgressAt;
        private static long _nodeLastRevision = -1L;
        private static float _nodeLastDistance = -1f;
        private static int _nodeStrikes;

        /// <summary>Tarefa de coleta em andamento.</summary>
        internal static bool IsActive => _active;

        /// <summary>Recurso pedido (nome do prefab do item, ex.: <c>Wood</c>).</summary>
        internal static string ResourceName => _resource;

        /// <summary>Quantidade restante (0 = sem limite).</summary>
        internal static int Remaining => _remaining;

        /// <summary>Total já coletado na tarefa (soma dos stacks pegados do chão).</summary>
        internal static int Collected => _collected;

        /// <summary>True quando a tarefa tem quantidade alvo (0 no comando = sem limite).</summary>
        internal static bool HasLimit => _hasLimit;

        /// <summary>Nó em que o companheiro está trabalhando agora (null se nenhum).</summary>
        internal static GameObject Node => _node;

        internal static void Configure(
            ConfigEntry<KeyCode> harvestKey,
            ConfigEntry<string> defaultResource,
            ConfigEntry<int> defaultAmount,
            ConfigEntry<float> scanRadius,
            ConfigEntry<float> scanInterval,
            ConfigEntry<bool> pauseOnCombat,
            ConfigEntry<bool> collectDrops,
            ConfigEntry<string> layerNames)
        {
            _harvestKey = harvestKey;
            _defaultResource = defaultResource;
            _defaultAmount = defaultAmount;
            _scanRadius = scanRadius;
            _scanInterval = scanInterval;
            _pauseOnCombat = pauseOnCombat;
            _collectDrops = collectDrops;
            _layerNames = layerNames;

            Plugin.Log.LogInfo(
                $"[ModIA] Coleta: tecla = {HarvestKey} | recurso padrão = '{DefaultResource}' | " +
                $"quantidade padrão = {DefaultAmount} | raio = {ScanRadius} m | " +
                $"varredura = {ScanInterval} s | pausar em combate = {PauseOnCombat} | " +
                $"catar drops = {CollectDrops} | camadas = '{LayerNames}'.");
        }

        /// <summary>Solta o estado estático (unload do plugin / fim do mundo).</summary>
        internal static void Shutdown()
        {
            _active = false;
            _hasLimit = false;
            _resource = string.Empty;
            _remaining = 0;
            _collected = 0;
            _node = null;
            _pausedForCombat = false;
            _nodeStrikes = 0;
            _nodeLastDistance = -1f;
            _nodeLastRevision = -1L;
            CompanionHarvestScanner.Shutdown();
        }

        // ---------------------------------------------------------------------
        // Tecla (inicia o recurso padrão / para a coleta)
        // ---------------------------------------------------------------------

        /// <summary>Chamado a cada frame pelo <see cref="Plugin"/> (sai rápido fora do mundo).</summary>
        internal static void Tick()
        {
            if (_harvestKey == null || CompanionManager.InputBlocked())
            {
                return;
            }

            if (!ZInput.GetKeyDown(_harvestKey.Value, false))
            {
                return;
            }

            if (_active)
            {
                Stop($"tecla {_harvestKey.Value}");
                return;
            }

            if (!StartDefault($"tecla {_harvestKey.Value}", out string error) && error != null)
            {
                ShowMessage(error, MessageHud.MessageType.Center);
            }
        }

        // ---------------------------------------------------------------------
        // Entrada: comandos / tecla
        // ---------------------------------------------------------------------

        /// <summary>Inicia a coleta do recurso padrão (tecla configurável).</summary>
        internal static bool StartDefault(string source, out string error)
        {
            return Start(CompanionManager.ActiveCompanion, DefaultResource, DefaultAmount, source, out error);
        }

        /// <summary>
        /// Inicia (ou reinicia) a tarefa de coleta. Devolve <c>false</c> com <paramref name="error"/>
        /// preenchido quando não é possível começar (sem companheiro, recurso desconhecido).
        /// </summary>
        internal static bool Start(
            CompanionAI companion,
            string requested,
            int amount,
            string source,
            out string error)
        {
            error = null;

            if (companion == null)
            {
                error = "[ModIA] Nenhum companheiro no mundo (spawne com a tecla configurada).";
                return false;
            }

            if (!TryResolveResource(requested, out string resource, out error))
            {
                return false;
            }

            if (!companion.IsLocallyOwned)
            {
                // A IA roda no dono do ZDO: num cliente sem autoridade a tarefa não anda.
                Plugin.Log.LogWarning(
                    "[ModIA] Sem autoridade sobre o companheiro (cliente): a coleta só roda no " +
                    "host/dono do ZDO.");
            }

            if (!CollectDrops && amount > 0)
            {
                Plugin.Log.LogWarning(
                    "[ModIA] Coleta com quantidade exige catar os drops (CatarDrops = true); " +
                    "a quantidade não vai fechar.");
            }

            _active = true;
            _hasLimit = amount > 0;
            _resource = resource;
            _remaining = Mathf.Max(0, amount);
            _collected = 0;
            _startedAt = Time.time;
            _pausedForCombat = false;
            ClearNode(null);

            CompanionHarvestScanner.ForgetBlacklist();

            string limit = _remaining > 0 ? $"{_remaining}x" : "sem limite";
            Plugin.Log.LogInfo(
                $"[ModIA] Coleta iniciada: '{resource}' ({limit}) por {source} " +
                $"(raio {ScanRadius} m, recurso pedido: '{requested}').");
            ShowMessage($"Coleta: {resource} ({limit})", MessageHud.MessageType.TopLeft);
            return true;
        }

        /// <summary>
        /// Encerra a tarefa (volta a seguir o jogador). <paramref name="reason"/> explica no log
        /// (fim normal, comando, meta atingida, travamento…).
        /// </summary>
        internal static void Stop(string reason)
        {
            if (!_active)
            {
                return;
            }

            float minutes = (Time.time - _startedAt) / 60f;
            int total = _collected;

            _active = false;
            _hasLimit = false;
            _remaining = 0;
            _pausedForCombat = false;
            ClearNode(null);
            CompanionHarvestScanner.ForgetBlacklist();

            if (total > 0)
            {
                Plugin.Log.LogInfo(
                    $"[ModIA] Coleta encerrada ({reason}): {total}x '{_resource}' em " +
                    $"{minutes:0.#} min.");
                ShowMessage($"Coleta encerrada: {total}x {_resource}", MessageHud.MessageType.TopLeft);
            }
            else
            {
                Plugin.Log.LogInfo($"[ModIA] Coleta encerrada ({reason}) sem nada coletado.");
                ShowMessage("Coleta encerrada: nada foi coletado", MessageHud.MessageType.TopLeft);
            }

            _resource = string.Empty;
            _collected = 0;
        }

        // ---------------------------------------------------------------------
        // Nó atual
        // ---------------------------------------------------------------------

        /// <summary>
        /// Define o nó que o companheiro vai trabalhar. Zera o contador de travamento e fotografa o
        /// estado inicial (ZDO/posição) para medir progresso depois.
        /// </summary>
        internal static void SetNode(GameObject node)
        {
            _node = node;
            _nodeStrikes = 0;
            _nodeLastProgressAt = Time.time;
            _nodeLastRevision = node != null ? CompanionHarvestScanner.GetRevision(node) : -1L;

            CompanionAI companion = CompanionManager.ActiveCompanion;
            _nodeLastDistance = node != null && companion != null
                ? Vector3.Distance(node.transform.position, companion.transform.position)
                : -1f;
        }

        /// <summary>Larga o nó atual (destruído, distante, tipo errado, combate…).</summary>
        internal static void ClearNode(string reason)
        {
            if (_node == null)
            {
                return;
            }

            if (reason != null)
            {
                Plugin.Log.LogInfo($"[ModIA] Coleta: nó '{_node.name}' largado ({reason}).");
            }

            _node = null;
            _nodeStrikes = 0;
            _nodeLastDistance = -1f;
            _nodeLastRevision = -1L;
        }

        /// <summary>
        /// Avisa que demos um golpe no nó. Se o ZDO dele mudou (dano aplicado) conta como
        /// progresso. É o que evita "bater em pedra sem picareta" para sempre: sem progresso
        /// nenhum, o nó é abandonado e posto na lista negra.
        /// </summary>
        internal static void RegisterStrike(GameObject node)
        {
            if (node == null || node != _node)
            {
                return;
            }

            _nodeStrikes++;

            long revision = CompanionHarvestScanner.GetRevision(node);
            if (revision < 0L)
            {
                // Sem ZDO legível não dá para julgar: considera progresso (não abandona por engano).
                _nodeLastProgressAt = Time.time;
                return;
            }

            if (revision != _nodeLastRevision)
            {
                _nodeLastRevision = revision;
                _nodeLastProgressAt = Time.time;
            }
        }

        /// <summary>
        /// Atualiza a medida de progresso por deslocamento: enquanto o companheiro se aproxima do
        /// nó ele está "trabalhando"; parado no mesmo lugar por <see cref="NodeGiveUpSeconds"/>,
        /// consideramos que travou (parede, água, nó impossível).
        /// </summary>
        internal static void ReportDistance(GameObject node, float distance)
        {
            if (node == null || node != _node)
            {
                return;
            }

            if (distance < _nodeLastDistance - 0.5f || _nodeLastDistance < 0f)
            {
                _nodeLastDistance = distance;
                _nodeLastProgressAt = Time.time;
                return;
            }

            if (distance < _nodeLastDistance)
            {
                _nodeLastDistance = distance;
            }
        }

        /// <summary>
        /// True quando vale desistir do nó atual (não chega, travou, ou a ferramenta não faz dano).
        /// <paramref name="why"/> descreve o motivo para o log.
        /// </summary>
        internal static bool ShouldGiveUpNode(out string why)
        {
            why = null;

            if (_node == null || Time.time - _nodeLastProgressAt <= NodeGiveUpSeconds)
            {
                return false;
            }

            why = _nodeStrikes > 0
                ? $"sem progresso por {NodeGiveUpSeconds:0} s após {_nodeStrikes} golpe(s)"
                : $"não consegui chegar no nó em {NodeGiveUpSeconds:0} s";
            return true;
        }

        /// <summary>Marca o nó como inalcançável/impossível (não será escolhido de novo por um tempo).</summary>
        internal static void BlacklistNode(GameObject node, string reason)
        {
            if (node == null)
            {
                return;
            }

            CompanionHarvestScanner.Blacklist(node, BlacklistSeconds);
            Plugin.Log.LogWarning(
                $"[ModIA] Coleta: desisti de '{node.name}' ({reason}); " +
                $"vetado por {BlacklistSeconds:0} s.");
            ClearNode(null);
        }

        // ---------------------------------------------------------------------
        // Combate x coleta
        // ---------------------------------------------------------------------

        /// <summary>
        /// Chamado quando o combate vence a coleta (Defensive/Aggressive com alvo hostil). A tarefa
        /// continua viva: assim que o alvo sumir, o companheiro volta a coletar.
        /// </summary>
        internal static void PauseForCombat()
        {
            if (_pausedForCombat)
            {
                return;
            }

            _pausedForCombat = true;
            if (_node != null)
            {
                Plugin.Log.LogInfo(
                    $"[ModIA] Coleta pausada: combate em andamento (alvo hostil). Nó '{_node.name}' largado.");
            }

            ClearNode(null);
        }

        /// <summary>Retoma a coleta depois de um combate (chamado pela IA quando perde o alvo).</summary>
        internal static void ResumeFromCombat()
        {
            if (!_pausedForCombat)
            {
                return;
            }

            _pausedForCombat = false;
            Plugin.Log.LogInfo("[ModIA] Coleta retomada (combate terminou).");
        }

        // ---------------------------------------------------------------------
        // Progresso
        // ---------------------------------------------------------------------

        /// <summary>
        /// Contabiliza itens pegos do chão (é o único contador honesto: o que cai da árvore/pedra é
        /// que vira estoque). Fecha a tarefa quando a quantidade pedida é atingida.
        /// </summary>
        internal static void NotifyCollected(int amount, string prefabName)
        {
            if (amount <= 0)
            {
                return;
            }

            _collected += amount;

            if (_remaining > 0)
            {
                _remaining = Mathf.Max(0, _remaining - amount);
            }

            Plugin.Log.LogInfo(
                $"[ModIA] Coleta: +{amount}x '{prefabName}' ({_collected} no total" +
                (_remaining > 0 ? $", faltam {_remaining}" : string.Empty) + ").");

            if (_hasLimit && _remaining == 0)
            {
                Stop("meta atingida");
            }
        }

        // ---------------------------------------------------------------------
        // Recursos (nome pedido -> prefab do item)
        // ---------------------------------------------------------------------

        /// <summary>
        /// Traduz o que o jogador digitou no <b>nome do prefab do item</b> que quer acumular.
        ///
        /// <para>
        /// Ordem: <b>lista curada</b> (<see cref="CompanionHarvestResources"/>: apelidos PT/EN, nome
        /// do prefab e nome PT-BR) → nome de prefab exato no <see cref="ObjectDB"/> → nome
        /// <i>localizado</i> do item no idioma do jogo. O fallback genérico garante que
        /// <b>qualquer</b> nome de item continue funcionando, mesmo fora da lista curada.
        /// </para>
        /// </summary>
        internal static bool TryResolveResource(string requested, out string resource, out string error)
        {
            resource = null;
            error = null;

            string input = (requested ?? string.Empty).Trim();
            if (input.Length == 0)
            {
                error = "[ModIA] Informe o recurso. Ex.: modia_gather wood 20 | pedra | raspberry";
                return false;
            }

            // 1) Lista curada (é o que faz "pedra", "silex", "cobre" e "Madeira" caírem no item certo).
            if (CompanionHarvestResources.TryResolve(input, out resource))
            {
                return true;
            }

            // 2) Fallback genérico: nome exato de prefab de item no ObjectDB.
            string prefab = FindItemPrefabName(input);
            if (prefab != null)
            {
                resource = prefab;
                return true;
            }

            // 3) Última tentativa: nome localizado do item (ex.: "Madeira" no cliente em português).
            string byLocalizedName = FindItemByLocalizedName(Fold(input));
            if (byLocalizedName != null)
            {
                resource = byLocalizedName;
                return true;
            }

            error =
                $"[ModIA] Recurso '{input}' não encontrado no ObjectDB. " +
                "Use o nome do item (Wood, Stone, Raspberry, Resin...) ou modia_gather sem argumento " +
                "para ver a ajuda.";
            return false;
        }

        /// <summary>
        /// Ajuda do comando: uso, estado atual e a lista curada de recursos (nomes localizados),
        /// agrupada por categoria.
        /// </summary>
        internal static List<string> GetHelp()
        {
            List<string> lines = new List<string>
            {
                "[ModIA] modia_gather <recurso> [qtd] - inicia a coleta (ex.: modia_gather wood 20).",
                "[ModIA] modia_stop - para a coleta e volta a seguir o jogador.",
                "[ModIA] Sem [qtd] o companheiro coleta enquanto houver nó no raio.",
                $"[ModIA] Tecla {HarvestKey} inicia/para a coleta do recurso padrão " +
                $"('{DefaultResource}', quantidade {DefaultAmount}).",
                "[ModIA] Recursos conhecidos (também aceita qualquer nome de item):",
            };

            lines.AddRange(CompanionHarvestResources.DescribeForHelp());

            if (_active)
            {
                string limit = _remaining > 0 ? $"faltam {_remaining}" : "sem limite";
                lines.Add($"[ModIA] Coleta ativa: '{_resource}' ({limit}), {_collected} coletado(s).");
            }

            return lines;
        }

        private static string FindItemPrefabName(string name)
        {
            ObjectDB db = ObjectDB.instance;
            if (db == null)
            {
                return null;
            }

            GameObject exact = db.GetItemPrefab(name);
            if (exact != null)
            {
                return Utils.GetPrefabName(exact.name);
            }

            List<GameObject> items = db.m_items;
            if (items == null)
            {
                return null;
            }

            for (int i = 0; i < items.Count; i++)
            {
                GameObject item = items[i];
                if (item == null)
                {
                    continue;
                }

                string prefabName = Utils.GetPrefabName(item.name);
                if (string.Equals(prefabName, name, StringComparison.OrdinalIgnoreCase))
                {
                    return prefabName;
                }
            }

            return null;
        }

        private static string FindItemByLocalizedName(string foldedInput)
        {
            ObjectDB db = ObjectDB.instance;
            if (db == null || db.m_items == null)
            {
                return null;
            }

            Localization localization = Localization.instance;
            if (localization == null)
            {
                return null;
            }

            List<GameObject> items = db.m_items;
            for (int i = 0; i < items.Count; i++)
            {
                GameObject item = items[i];
                ItemDrop drop = item != null ? item.GetComponent<ItemDrop>() : null;
                if (drop == null || drop.m_itemData == null || drop.m_itemData.m_shared == null)
                {
                    continue;
                }

                string sharedName = drop.m_itemData.m_shared.m_name;
                if (string.IsNullOrEmpty(sharedName))
                {
                    continue;
                }

                string localized = localization.Localize(sharedName);
                if (localized != null && Fold(localized) == foldedInput)
                {
                    return Utils.GetPrefabName(item.name);
                }
            }

            return null;
        }

        /// <summary>
        /// Minúsculas sem acentos (para "Colheita", "colheita", "COLHEITA" caírem no mesmo apelido).
        /// </summary>
        internal static string Fold(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return string.Empty;
            }

            char[] buffer = text.Trim().ToLowerInvariant().ToCharArray();
            for (int i = 0; i < buffer.Length; i++)
            {
                switch (buffer[i])
                {
                    case 'á':
                    case 'à':
                    case 'â':
                    case 'ã':
                    case 'ä':
                        buffer[i] = 'a';
                        break;
                    case 'é':
                    case 'è':
                    case 'ê':
                    case 'ë':
                        buffer[i] = 'e';
                        break;
                    case 'í':
                    case 'ì':
                    case 'î':
                    case 'ï':
                        buffer[i] = 'i';
                        break;
                    case 'ó':
                    case 'ò':
                    case 'ô':
                    case 'õ':
                    case 'ö':
                        buffer[i] = 'o';
                        break;
                    case 'ú':
                    case 'ù':
                    case 'û':
                    case 'ü':
                        buffer[i] = 'u';
                        break;
                    case 'ç':
                        buffer[i] = 'c';
                        break;
                }
            }

            return new string(buffer);
        }

        internal static void ShowMessage(string text, MessageHud.MessageType type)
        {
            MessageHud hud = MessageHud.instance;
            if (hud != null)
            {
                hud.ShowMessage(type, text);
            }
        }

        /// <summary>Momento (<see cref="Time.time"/>) do último aviso de ferramenta ausente.</summary>
        private static float _lastToolWarnAt = -999f;

        /// <summary>
        /// Avisa no HUD que o recurso pedido exige uma ferramenta que o companheiro não tem
        /// (ex.: picareta para pedra/minério). Rate-limited (5 s): o HUD não pode virar spam.
        /// </summary>
        internal static void NotifyMissingTool(string resource)
        {
            if (Time.time - _lastToolWarnAt < 5f)
            {
                return;
            }

            _lastToolWarnAt = Time.time;
            ShowMessage(
                $"Sem ferramenta capaz para {CompanionHarvestResources.DisplayName(resource)} " +
                "(precisa de machado/picareta)",
                MessageHud.MessageType.TopLeft);
        }
    }
}

using System;
using System.Collections.Generic;
using System.Globalization;
using BepInEx.Configuration;
using UnityEngine;

namespace ModAIValheim.Companion
{
    /// <summary>
    /// Aparência padrão do companheiro (Fase 2.3b: "viking decente").
    ///
    /// <para>
    /// O corpo base é o do jogador (<c>'Player'</c>, ver <see cref="CompanionPrefab"/>), então o
    /// companheiro já nasce com o corpo do viking e a armadura de peito/pernas visível. O que
    /// falta só com o clone é a "cara": <c>VisEquipment</c> renderiza cabelo/barba apenas quando
    /// o ZDO tem os hashes (<c>HairItem</c>/<c>BeardItem</c>) e as cores (<c>SkinColor</c>/
    /// <c>HairColor</c>). Este módulo aplica esses valores logo depois do spawn, no dono do ZDO,
    /// de onde eles replicam para os demais clientes.
    /// </para>
    ///
    /// <para>
    /// No Valheim 1.0.17 a API do <see cref="VisEquipment"/> é <b>hash-based</b>:
    /// <c>SetHairItem(int itemHash)</c>/<c>SetBeardItem(int itemHash)</c> recebem
    /// <c>Utils.GetPrefabName(prefab).GetStableHashCode()</c>, e as cores vão por
    /// <c>SetSkinColor(Vector3)</c>/<c>SetHairColor(Vector3)</c> (RGB 0..1 —
    /// <c>Utils.Vec3ToColor</c> é um cast direto para <c>Color</c>).
    /// </para>
    ///
    /// <para>
    /// Os cabelos/barbas vanilla são itens de <c>ObjectDB</c> do tipo
    /// <c>ItemData.ItemType.Customization</c> chamados <c>Hair1..Hair38</c> e
    /// <c>Beard1..Beard26</c> (mais as variantes com <c>_</c> e os "None"). Nada aqui roda por
    /// frame: só no spawn (ou ao adotar um companheiro já existente).
    /// </para>
    /// </summary>
    internal static class CompanionAppearance
    {
        /// <summary>Valores que desligam o cabelo/barba na config.</summary>
        private static readonly string[] OffTokens = { "none", "off", "0", "-" };

        /// <summary>Cor de pele assumida quando a config está vazia/inválida.</summary>
        internal static readonly Vector3 DefaultSkinColor = new Vector3(0.92f, 0.82f, 0.74f);

        /// <summary>Cor de cabelo/barba assumida quando a config está vazia/inválida.</summary>
        internal static readonly Vector3 DefaultHairColor = new Vector3(0.62f, 0.45f, 0.3f);

        /// <summary>
        /// Valor de config que representa "deixe o jogo escolher" (é o default das entries). O
        /// <see cref="ResolveCustomizationPrefab"/> então pega o primeiro item utilizável.
        /// </summary>
        internal const string AutoValue = "";

        /// <summary>Valor de config que desliga o cabelo/barba (cabeça careca).</summary>
        internal const string NoneValue = "none";

        /// <summary>Prefixo dos itens de cabelo no ObjectDB (<c>Hair1</c>…).</summary>
        internal const string HairPrefix = "Hair";

        /// <summary>Prefixo dos itens de barba no ObjectDB (<c>Beard1</c>…).</summary>
        internal const string BeardPrefix = "Beard";
        private static ConfigEntry<bool> _enabled;
        private static ConfigEntry<string> _hairPrefab;
        private static ConfigEntry<string> _beardPrefab;
        private static ConfigEntry<string> _skinColor;
        private static ConfigEntry<string> _hairColor;

        internal static void Configure(
            ConfigEntry<bool> enabled,
            ConfigEntry<string> hairPrefab,
            ConfigEntry<string> beardPrefab,
            ConfigEntry<string> skinColor,
            ConfigEntry<string> hairColor)
        {
            _enabled = enabled;
            _hairPrefab = hairPrefab;
            _beardPrefab = beardPrefab;
            _skinColor = skinColor;
            _hairColor = hairColor;
        }

        // ---------------------------------------------------------------------
        // API de leitura/escrita ao vivo (menu do mod, Fase 2.6b)
        // ---------------------------------------------------------------------

        /// <summary>Aparência ligada na config (<c>Aparencia/AparenciaPadrao</c>).</summary>
        internal static bool Enabled => _enabled == null || _enabled.Value;

        /// <summary>Cor de pele atual (valor da config; cai no padrão se inválida).</summary>
        internal static Vector3 SkinColor => ParseColor(_skinColor, DefaultSkinColor, "CorPele");

        /// <summary>Cor de cabelo/barba atual (valor da config; cai no padrão se inválida).</summary>
        internal static Vector3 HairColor => ParseColor(_hairColor, DefaultHairColor, "CorCabelo");

        /// <summary>Cabelo configurado (vazio = automático; "none" = careca).</summary>
        internal static string HairPrefabName => _hairPrefab != null ? _hairPrefab.Value : string.Empty;

        /// <summary>Barba configurada (vazio = automática; "none" = sem barba).</summary>
        internal static string BeardPrefabName => _beardPrefab != null ? _beardPrefab.Value : string.Empty;

        /// <summary>Liga/desliga a aparência e reaplica no companheiro ativo (grava a config).</summary>
        internal static void SetEnabled(bool value)
        {
            if (_enabled != null)
            {
                _enabled.Value = value;
            }

            RefreshLive();
        }

        /// <summary>Troca a cor de pele (grava a config) e aplica no companheiro ativo.</summary>
        internal static void SetSkinColor(Vector3 color)
        {
            WriteColor(_skinColor, color);

            VisEquipment visEq = ActiveVisEquipment();
            if (visEq != null)
            {
                visEq.SetSkinColor(color);
            }
        }

        /// <summary>Troca a cor de cabelo/barba (grava a config) e aplica no companheiro ativo.</summary>
        internal static void SetHairColor(Vector3 color)
        {
            WriteColor(_hairColor, color);

            VisEquipment visEq = ActiveVisEquipment();
            if (visEq != null)
            {
                visEq.SetHairColor(color);
            }
        }

        /// <summary>Troca o cabelo (grava a config) e aplica no companheiro ativo.</summary>
        internal static void SetHairPrefab(string prefabName)
        {
            if (_hairPrefab != null)
            {
                _hairPrefab.Value = prefabName ?? string.Empty;
            }

            VisEquipment visEq = ActiveVisEquipment();
            if (visEq != null)
            {
                ApplyCustomizationItem(visEq, _hairPrefab, HairPrefix, true);
            }
        }

        /// <summary>Troca a barba (grava a config) e aplica no companheiro ativo.</summary>
        internal static void SetBeardPrefab(string prefabName)
        {
            if (_beardPrefab != null)
            {
                _beardPrefab.Value = prefabName ?? string.Empty;
            }

            VisEquipment visEq = ActiveVisEquipment();
            if (visEq != null)
            {
                ApplyCustomizationItem(visEq, _beardPrefab, BeardPrefix, false);
            }
        }

        /// <summary>
        /// Reaplica do zero a aparência no companheiro ativo (usado ao ligar/desligar). Ligada,
        /// aplica a config; desligada, devolve o visual "cru" do prefab (sem cabelo/barba e com as
        /// cores neutras do jogo).
        /// </summary>
        internal static void RefreshLive()
        {
            VisEquipment visEq = ActiveVisEquipment();
            if (visEq == null)
            {
                return;
            }

            if (Enabled)
            {
                ApplyToEquipment(visEq);
                return;
            }

            visEq.SetSkinColor(Vector3.one);
            visEq.SetHairColor(Vector3.one);
            visEq.SetHairItem(0);
            visEq.SetBeardItem(0);
            Plugin.Log.LogInfo("[ModIA] Aparência: desligada (visual padrão do jogo).");
        }

        /// <summary>
        /// Nomes dos itens de customização utilizáveis com o prefixo dado (<c>Hair</c>/<c>Beard</c>),
        /// ordenados — é a lista oferecida pelo menu do mod. Exclui os "None" e os que não têm
        /// <c>attach</c> (que o <c>VisEquipment</c> não sabe instanciar). Uma varredura por abertura
        /// do menu, nunca por frame.
        /// </summary>
        internal static List<string> GetCustomizationNames(string prefix)
        {
            List<string> names = new List<string>();

            ObjectDB database = ObjectDB.instance;
            if (database == null)
            {
                return names;
            }

            List<ItemDrop> items = database.GetAllItems(ItemDrop.ItemData.ItemType.Customization, prefix);
            for (int i = 0; i < items.Count; i++)
            {
                ItemDrop item = items[i];
                if (item == null)
                {
                    continue;
                }

                string name = Utils.GetPrefabName(item.gameObject);
                if (name.IndexOf("none", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    continue;
                }

                if (!IsAttachableCustomization(item.gameObject))
                {
                    continue;
                }

                if (!names.Contains(name))
                {
                    names.Add(name);
                }
            }

            names.Sort(StringComparer.OrdinalIgnoreCase);
            return names;
        }

        private static VisEquipment ActiveVisEquipment()
        {
            CompanionAI companion = CompanionManager.ActiveCompanion;
            if (companion == null)
            {
                return null;
            }

            VisEquipment visEq = companion.GetComponent<VisEquipment>();
            if (visEq == null)
            {
                Plugin.Log.LogWarning(
                    "[ModIA] Companheiro sem VisEquipment; mudança de aparência ignorada.");
            }

            return visEq;
        }

        private static void WriteColor(ConfigEntry<string> entry, Vector3 color)
        {
            if (entry == null)
            {
                return;
            }

            entry.Value = string.Format(
                CultureInfo.InvariantCulture,
                "{0:0.###};{1:0.###};{2:0.###}",
                color.x,
                color.y,
                color.z);
        }

        /// <summary>
        /// Aplica a aparência padrão na instância recém-spawnada. Tolerante a falhas: qualquer
        /// coisa ausente só gera aviso no log (o companheiro continua jogável).
        /// </summary>
        internal static void Apply(CompanionAI companion)
        {
            if (companion == null)
            {
                return;
            }

            if (_enabled != null && !_enabled.Value)
            {
                Plugin.Log.LogInfo("[ModIA] Aparência padrão desligada na config.");
                return;
            }

            VisEquipment visEq = companion.GetComponent<VisEquipment>();
            if (visEq == null)
            {
                Plugin.Log.LogWarning(
                    "[ModIA] Companheiro sem VisEquipment; aparência padrão não aplicada.");
                return;
            }

            ApplyToEquipment(visEq);
        }

        /// <summary>
        /// Aplica a aparência da config em um <see cref="VisEquipment"/> (usado no spawn e no
        /// refresh ao vivo do menu). Não checa <see cref="Enabled"/>: quem decide se aplica ou
        /// "limpa" é o chamador.
        /// </summary>
        private static void ApplyToEquipment(VisEquipment visEq)
        {
            ApplyColors(visEq, SkinColor, HairColor);

            ApplyCustomizationItem(visEq, _hairPrefab, HairPrefix, true);
            ApplyCustomizationItem(visEq, _beardPrefab, BeardPrefix, false);
        }

        /// <summary>
        /// Aplica a aparência vinda do <b>snapshot</b> do companheiro (Fase 2.7), usada na
        /// invocação quando existe estado salvo: o companheiro volta com o mesmo cabelo/barba e as
        /// mesmas cores de quando foi dispensado.
        ///
        /// <para>
        /// Não escreve na config de propósito — o snapshot é <i>histórico</i> (como o companheiro
        /// estava), não uma preferência do jogador; a preferência continua sendo a config, que o
        /// menu edita.
        /// </para>
        /// </summary>
        internal static void ApplySnapshot(CompanionAI companion, CompanionState state)
        {
            if (companion == null || state == null)
            {
                return;
            }

            VisEquipment visEq = companion.GetComponent<VisEquipment>();
            if (visEq == null)
            {
                Plugin.Log.LogWarning(
                    "[ModIA] Companheiro sem VisEquipment; aparência do snapshot não aplicada.");
                return;
            }

            if (!state.AppearanceEnabled)
            {
                visEq.SetSkinColor(Vector3.one);
                visEq.SetHairColor(Vector3.one);
                visEq.SetHairItem(0);
                visEq.SetBeardItem(0);
                Plugin.Log.LogInfo("[ModIA] Aparência do snapshot: desligada (visual padrão do jogo).");
                return;
            }

            // Snapshot vazio/corrompido (tudo zero) cai nas cores padrão em vez de pintar de preto.
            Vector3 skin = SanitizeColor(state.SkinColor, DefaultSkinColor);
            Vector3 hair = SanitizeColor(state.HairColor, DefaultHairColor);

            ApplyColors(visEq, skin, hair);

            ApplyCustomizationItem(visEq, state.Hair, HairPrefix, true);
            ApplyCustomizationItem(visEq, state.Beard, BeardPrefix, false);

            Plugin.Log.LogInfo(
                $"[ModIA] Aparência do snapshot aplicada (cabelo '{state.Hair}', " +
                $"barba '{state.Beard}').");
        }

        /// <summary>Cor válida: usa o padrão quando o valor do snapshot é nulo (tudo zero).</summary>
        private static Vector3 SanitizeColor(Vector3 value, Vector3 fallback)
        {
            return value == Vector3.zero ? fallback : value;
        }

        // ---------------------------------------------------------------------
        // Cores
        // ---------------------------------------------------------------------

        private static void ApplyColors(VisEquipment visEq, Vector3 skinColor, Vector3 hairColor)
        {
            // Os setters só escrevem no ZDO se mudou de valor; chamar de novo é inofensivo.
            visEq.SetSkinColor(skinColor);
            visEq.SetHairColor(hairColor);

            Plugin.Log.LogInfo(
                $"[ModIA] Aparência: pele = ({skinColor.x:0.##};{skinColor.y:0.##};{skinColor.z:0.##}), " +
                $"cabelo = ({hairColor.x:0.##};{hairColor.y:0.##};{hairColor.z:0.##}).");
        }

        /// <summary>Lê "r;g;b" (0..1, ponto decimal) e cai no padrão se o texto estiver inválido.</summary>
        private static Vector3 ParseColor(ConfigEntry<string> entry, Vector3 fallback, string label)
        {
            string text = entry?.Value;
            if (string.IsNullOrWhiteSpace(text))
            {
                return fallback;
            }

            string[] parts = text.IndexOf(';') >= 0
                ? text.Split(';')
                : text.Split(',');

            if (parts.Length < 3)
            {
                Plugin.Log.LogWarning(
                    $"[ModIA] {label} inválida ('{text}'); use \"r;g;b\" (ex.: 0.92;0.82;0.74). " +
                    "Usando o padrão.");
                return fallback;
            }

            float r;
            float g;
            float b;
            if (!TryParseComponent(parts[0], out r) ||
                !TryParseComponent(parts[1], out g) ||
                !TryParseComponent(parts[2], out b))
            {
                Plugin.Log.LogWarning(
                    $"[ModIA] {label} inválida ('{text}'); use números de 0 a 1 com ponto " +
                    "decimal (ex.: 0.92;0.82;0.74). Usando o padrão.");
                return fallback;
            }

            return new Vector3(Mathf.Clamp01(r), Mathf.Clamp01(g), Mathf.Clamp01(b));
        }

        private static bool TryParseComponent(string text, out float value)
        {
            return float.TryParse(
                text?.Trim(),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out value);
        }

        // ---------------------------------------------------------------------
        // Cabelo / barba
        // ---------------------------------------------------------------------

        private static void ApplyCustomizationItem(
            VisEquipment visEq,
            ConfigEntry<string> entry,
            string prefix,
            bool isHair)
        {
            ApplyCustomizationItem(visEq, entry?.Value, prefix, isHair);
        }

        /// <summary>
        /// Núcleo do cabelo/barba: recebe o valor direto (nome do prefab, "none" ou vazio =
        /// automático) para servir tanto a config quanto o snapshot (Fase 2.7).
        /// </summary>
        private static void ApplyCustomizationItem(
            VisEquipment visEq,
            string configuredValue,
            string prefix,
            bool isHair)
        {
            string configured = configuredValue?.Trim();
            string label = isHair ? "cabelo" : "barba";

            if (IsDisabled(configured))
            {
                // Hash 0 = sem item: o VisEquipment desanexa o cabelo/barba atual (é o "careca").
                if (isHair)
                {
                    visEq.SetHairItem(0);
                }
                else
                {
                    visEq.SetBeardItem(0);
                }

                Plugin.Log.LogInfo($"[ModIA] Aparência: {label} desligado.");
                return;
            }

            GameObject prefab = ResolveCustomizationPrefab(configured, prefix, label);
            if (prefab == null)
            {
                return;
            }

            string name = Utils.GetPrefabName(prefab);
            int hash = name.GetStableHashCode();

            if (isHair)
            {
                visEq.SetHairItem(hash);
            }
            else
            {
                visEq.SetBeardItem(hash);
            }

            Plugin.Log.LogInfo($"[ModIA] Aparência: {label} = '{name}' (hash {hash}).");
        }

        private static bool IsDisabled(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return false;
            }

            for (int i = 0; i < OffTokens.Length; i++)
            {
                if (string.Equals(value, OffTokens[i], StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>True se o texto de config significa "sem cabelo/barba" (usado pelo menu).</summary>
        internal static bool IsDisabledValue(string value)
        {
            return IsDisabled(value);
        }

        /// <summary>
        /// Se a config tem um nome, usa esse prefab; se estiver vazia, pega o primeiro item de
        /// customização do jogo que sirva (é o "default decente"). Em ambos os casos o prefab
        /// precisa ter o filho <c>attach</c>/<c>attach_skin</c>, que é de onde o
        /// <c>VisEquipment</c> instancia o cabelo.
        /// </summary>
        private static GameObject ResolveCustomizationPrefab(string configured, string prefix, string label)
        {
            ObjectDB database = ObjectDB.instance;
            if (database == null)
            {
                Plugin.Log.LogWarning(
                    $"[ModIA] ObjectDB indisponível; {label} padrão não aplicado.");
                return null;
            }

            if (!string.IsNullOrEmpty(configured))
            {
                GameObject exact = database.GetItemPrefab(configured);
                if (IsAttachableCustomization(exact))
                {
                    return exact;
                }

                Plugin.Log.LogWarning(
                    $"[ModIA] {label} '{configured}' não existe (ou não é um item de " +
                    "customização com 'attach'); caindo para o padrão do jogo.");
            }

            // Varrer os itens de customização é uma operação única por spawn (não é loop de frame).
            List<ItemDrop> items = database.GetAllItems(ItemDrop.ItemData.ItemType.Customization, prefix);
            GameObject fallback = null;

            for (int i = 0; i < items.Count; i++)
            {
                ItemDrop item = items[i];
                if (item == null)
                {
                    continue;
                }

                string name = Utils.GetPrefabName(item.gameObject);
                if (name.IndexOf("none", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    continue;
                }

                if (!IsAttachableCustomization(item.gameObject))
                {
                    continue;
                }

                // As variantes com '_' são versões alternativas do mesmo penteado; o jogo as
                // esconde da lista do barbeiro. Só usamos se não houver nenhuma "principal".
                if (name.IndexOf('_') >= 0)
                {
                    if (fallback == null)
                    {
                        fallback = item.gameObject;
                    }

                    continue;
                }

                return item.gameObject;
            }

            if (fallback == null)
            {
                Plugin.Log.LogWarning(
                    $"[ModIA] Nenhum item de customização '{prefix}*' utilizável em ObjectDB; " +
                    $"{label} não aplicado.");
            }

            return fallback;
        }

        private static bool IsAttachableCustomization(GameObject prefab)
        {
            if (prefab == null)
            {
                return false;
            }

            if (prefab.GetComponent<ItemDrop>() == null)
            {
                return false;
            }

            // VisEquipment.AttachItem procura exatamente esses filhos para instanciar o visual.
            return prefab.transform.Find("attach") != null || prefab.transform.Find("attach_skin") != null;
        }
    }
}

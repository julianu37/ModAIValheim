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

        private static readonly Vector3 DefaultSkinColor = new Vector3(0.92f, 0.82f, 0.74f);
        private static readonly Vector3 DefaultHairColor = new Vector3(0.62f, 0.45f, 0.3f);

        private const string HairPrefix = "Hair";
        private const string BeardPrefix = "Beard";

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

            ApplyColors(visEq, ParseColor(_skinColor, DefaultSkinColor, "CorPele"),
                ParseColor(_hairColor, DefaultHairColor, "CorCabelo"));

            ApplyCustomizationItem(visEq, _hairPrefab, HairPrefix, true);
            ApplyCustomizationItem(visEq, _beardPrefab, BeardPrefix, false);
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
            string configured = entry?.Value?.Trim();
            string label = isHair ? "cabelo" : "barba";

            if (IsDisabled(configured))
            {
                Plugin.Log.LogInfo($"[ModIA] Aparência: {label} desligado na config.");
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

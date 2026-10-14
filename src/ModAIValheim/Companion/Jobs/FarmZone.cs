using System;
using System.Globalization;
using UnityEngine;

namespace ModAIValheim.Companion.Jobs
{
    /// <summary>
    /// <b>Zona de fazenda</b> (Fase 2.16b): o pedaço de mundo onde o companheiro pode cultivar —
    /// um <b>centro + raio</b> (o "canteiro" do jogador) e, opcionalmente, a <b>cultura</b> a
    /// plantar ali.
    ///
    /// <para>
    /// O jogador define com <c>modia_farm set [raio]</c> no ponto onde está (a config
    /// <c>Jobs/FarmZona</c> guarda o resultado entre sessões) e limpa com <c>modia_farm clear</c>.
    /// Sem cultura fixada (<see cref="Crop"/> vazio) ele planta o que tiver de semente no
    /// inventário.
    /// </para>
    ///
    /// <para>
    /// Adaptado (lógica) de <c>src/Companions/Jobs/FarmZone.cs</c> do <i>Offline Companions
    /// Reborn</i> (MIT — ver <c>THIRD_PARTY_NOTICES.md</c>): lá a zona é um <b>quadrado</b>
    /// rotacionado (<c>HalfSize</c> + <c>Rotation</c>, com <c>WorldToLocal</c>/<c>LocalToWorld</c>
    /// para as fileiras de plantio) e o serializador guarda os cantos; aqui ela é um <b>círculo</b>
    /// (centro + raio), que é o que o comando pede e o que o jogador desenha com um "raio" na
    /// cabeça, serializado como texto simples na config.
    /// </para>
    /// </summary>
    internal struct FarmZone
    {
        /// <summary>Centro da zona (o ponto onde o jogador definiu, já no chão).</summary>
        internal Vector3 Center;

        /// <summary>Raio (m) da zona.</summary>
        internal float Radius;

        /// <summary>Prefab da semente fixada ("" = planta o que houver no inventário).</summary>
        internal string Crop;

        /// <summary>True quando a zona tem um raio válido (senão não há zona definida).</summary>
        internal bool IsValid => Radius > 0f;

        /// <summary>O ponto está dentro da zona (no plano XZ)?</summary>
        internal bool Contains(Vector3 pos)
        {
            float dx = pos.x - Center.x;
            float dz = pos.z - Center.z;
            return dx * dx + dz * dz <= Radius * Radius;
        }

        /// <summary>Distância no plano (XZ) do centro — o Y da zona não interessa.</summary>
        internal float FlatDistance(Vector3 pos)
        {
            float dx = pos.x - Center.x;
            float dz = pos.z - Center.z;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        /// <summary>
        /// Serializa a zona para a config (<c>x;y;z;raio;cultura</c>, invariante de cultura). O
        /// <c>;</c> é o separador porque nenhum nome de prefab do jogo o usa.
        /// </summary>
        internal string Serialize()
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "{0:0.##};{1:0.##};{2:0.##};{3:0.##};{4}",
                Center.x,
                Center.y,
                Center.z,
                Radius,
                Crop ?? string.Empty);
        }

        /// <summary>Lê a zona da config. Devolve <c>false</c> quando o texto não é uma zona válida.</summary>
        internal static bool TryParse(string text, out FarmZone zone)
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
            zone.Crop = parts.Length >= 5 && !string.IsNullOrEmpty(parts[4]) ? parts[4].Trim() : string.Empty;
            return zone.IsValid;
        }

        /// <summary>Descrição curta para o console/HUD (<c>centro 12,4 / raio 8 m / cultura X</c>).</summary>
        internal string Describe()
        {
            if (!IsValid)
            {
                return "nenhuma (use modia_farm set)";
            }

            string crop = string.IsNullOrEmpty(Crop) ? "automatica (a semente do inventario)" : Crop;
            return $"{Center.x:0.#}, {Center.z:0.#} (raio {Radius:0.#} m, cultura: {crop})";
        }
    }
}

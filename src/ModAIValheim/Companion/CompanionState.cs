using System;
using System.Collections.Generic;
using UnityEngine;

namespace ModAIValheim.Companion
{
    /// <summary>
    /// Retrato (snapshot) do companheiro (Fase 2.7): o que ele tinha no inventário, o que estava
    /// equipado, a aparência, o modo de combate e a vida no momento da captura.
    ///
    /// <para>
    /// O snapshot é tirado ao <b>dispensar</b> (tecla H), ao <b>morrer</b> e no <b>fim do mundo</b>
    /// (ver <see cref="CompanionStateStore"/>) e devolvido no próximo <b>invocar</b> (F1), para o
    /// companheiro voltar "o mesmo" em vez de nascer vazio com o loadout padrão.
    /// </para>
    ///
    /// <para>
    /// Os tipos são <c>public</c> e só usam campos públicos de propósito: é o contrato que o
    /// <see cref="JsonUtility"/> do Unity serializa/deserializa para o arquivo
    /// <c>BepInEx\config\modia.companion.state.json</c> (propriedades calculadas como
    /// <see cref="EquippedCount"/> são ignoradas na serialização).
    /// </para>
    ///
    /// <para>
    /// <b>Vida:</b> é capturada (<see cref="Health"/>/<see cref="MaxHealth"/>) só para
    /// diagnóstico — a restauração devolve o companheiro com a vida <b>cheia</b> (ver
    /// <see cref="CompanionStateStore.TryRestore"/>).
    /// </para>
    /// </summary>
    [Serializable]
    public sealed class CompanionState
    {
        /// <summary>Versão do formato do arquivo (para validar/descartar arquivos antigos).</summary>
        public const int CurrentVersion = 1;

        /// <summary>Versão do formato gravada no arquivo.</summary>
        public int Version = CurrentVersion;

        /// <summary>Inventário completo, na ordem em que os itens estavam.</summary>
        public List<CompanionItemState> Items = new List<CompanionItemState>();

        /// <summary>Aparência personalizada estava ligada (<c>Aparencia/AparenciaPadrao</c>).</summary>
        public bool AppearanceEnabled = true;

        /// <summary>Prefab do cabelo ("" = automático; "none" = careca).</summary>
        public string Hair = string.Empty;

        /// <summary>Prefab da barba ("" = automática; "none" = sem barba).</summary>
        public string Beard = string.Empty;

        /// <summary>Cor de pele (r;g;b de 0 a 1).</summary>
        public float SkinR;
        public float SkinG;
        public float SkinB;

        /// <summary>Cor do cabelo/barba (r;g;b de 0 a 1).</summary>
        public float HairR;
        public float HairG;
        public float HairB;

        /// <summary>Modo de combate no momento da captura (<c>Passive</c>/<c>Defensive</c>/<c>Aggressive</c>).</summary>
        public string Mode = string.Empty;

        /// <summary>Vida atual na captura (informativo: não é restaurada).</summary>
        public float Health;

        /// <summary>Vida máxima na captura (informativo).</summary>
        public float MaxHealth;

        /// <summary>O que disparou a captura ("dispensa", "morte", "fim do mundo"…).</summary>
        public string Source = string.Empty;

        /// <summary>Quando a captura foi feita (ISO-8601, UTC) — só para leitura humana.</summary>
        public string SavedAtUtc = string.Empty;

        /// <summary>Quantos itens do snapshot estavam equipados.</summary>
        public int EquippedCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < Items.Count; i++)
                {
                    if (Items[i] != null && Items[i].Equipped)
                    {
                        count++;
                    }
                }

                return count;
            }
        }

        /// <summary>Cor de pele como <see cref="Vector3"/> (não é serializada: é o acesso tipado).</summary>
        public Vector3 SkinColor
        {
            get { return new Vector3(SkinR, SkinG, SkinB); }
            set
            {
                SkinR = value.x;
                SkinG = value.y;
                SkinB = value.z;
            }
        }

        /// <summary>Cor do cabelo/barba como <see cref="Vector3"/> (não é serializada).</summary>
        public Vector3 HairColor
        {
            get { return new Vector3(HairR, HairG, HairB); }
            set
            {
                HairR = value.x;
                HairG = value.y;
                HairB = value.z;
            }
        }
    }

    /// <summary>
    /// Um item do inventário do companheiro dentro do snapshot. Guarda o <b>prefab</b> (nome), a
    /// pilha, a qualidade, a durabilidade, o <c>worldLevel</c> e a variante — o suficiente para
    /// recriar o item a partir do <c>ObjectDB</c> com as mesmas características (Fase 2.7).
    /// </summary>
    [Serializable]
    public sealed class CompanionItemState
    {
        /// <summary>Nome do prefab do item no <c>ObjectDB</c> (ex.: <c>Wood</c>, <c>AxeStone</c>).</summary>
        public string Prefab = string.Empty;

        /// <summary>Tamanho da pilha.</summary>
        public int Stack = 1;

        /// <summary>Qualidade (1..<c>m_maxQuality</c>).</summary>
        public int Quality = 1;

        /// <summary>Durabilidade atual (0 = usar a máxima do item).</summary>
        public float Durability;

        /// <summary>Nível de mundo do item (<c>m_worldLevel</c>).</summary>
        public int WorldLevel;

        /// <summary>Variante visual do item (<c>m_variant</c>).</summary>
        public int Variant;

        /// <summary>True se o item estava equipado quando o snapshot foi tirado.</summary>
        public bool Equipped;
    }
}

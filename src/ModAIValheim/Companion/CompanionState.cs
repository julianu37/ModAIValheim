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
    /// <b>Fase 2.13:</b> o retrato também guarda o que faz dele um <i>membro da equipe</i> — as
    /// <b>comidas ativas</b> (com o tempo de queima que faltava), a <b>stamina</b> e as
    /// <b>perícias</b> (<see cref="Foods"/>/<see cref="Stamina"/>/<see cref="Skills"/>). Esses três
    /// campos são <b>aditivos</b>: um arquivo antigo (formato v1 sem eles) carrega normalmente, só
    /// sem comida/stamina/skills guardadas — o companheiro volta com os itens, como antes.
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

        /// <summary>
        /// Comidas ativas no corpo (Fase 2.13), com o tempo que ainda faltava queimar. Campos
        /// <b>aditivos</b>: um arquivo gravado antes da 2.13 não tem esta chave e é carregado com a
        /// lista vazia (nada de perder o companheiro guardado por causa do formato).
        /// </summary>
        public List<CompanionFoodState> Foods = new List<CompanionFoodState>();

        /// <summary>Stamina na captura (Fase 2.13); <c>&lt; 0</c> = sem dado (usa o teto cheio).</summary>
        public float Stamina = -1f;

        /// <summary>Perícias do companheiro (Fase 2.13), também aditivas.</summary>
        public List<CompanionSkillState> Skills = new List<CompanionSkillState>();

        /// <summary>Vida atual na captura (informativo: não é restaurada).</summary>
        public float Health;

        /// <summary>Vida máxima na captura (informativo).</summary>
        public float MaxHealth;

        /// <summary>O que disparou a captura ("dispensa", "morte", "fim do mundo"…).</summary>
        public string Source = string.Empty;

        /// <summary>Quando a captura foi feita (ISO-8601, UTC) — só para leitura humana.</summary>
        public string SavedAtUtc = string.Empty;

        /// <summary>Quantas comidas estavam ativas no momento da captura.</summary>
        public int FoodCount => Foods != null ? Foods.Count : 0;

        /// <summary>Quantas perícias tinham nível/acumulador no momento da captura.</summary>
        public int SkillCount => Skills != null ? Skills.Count : 0;

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

    /// <summary>
    /// Uma comida ativa no corpo do companheiro dentro do snapshot (Fase 2.13). Guarda os bônus já
    /// resolvidos (<c>m_food</c>/<c>m_foodStamina</c>/<c>m_foodEitr</c>/<c>m_foodRegen</c>) e o tempo
    /// de queima, porque o companheiro não tem <c>m_foods</c> do jogo: os slots são nossos
    /// (<see cref="CompanionFood"/>) e precisam ser reescritos do zero ao re-invocar.
    /// </summary>
    [Serializable]
    public sealed class CompanionFoodState
    {
        /// <summary>Prefab do item (ex.: <c>CookedMeat</c>) — só para leitura humana/log.</summary>
        public string Prefab = string.Empty;

        /// <summary>Nome localizado da comida (<c>m_shared.m_name</c>) — é a chave dos slots.</summary>
        public string Name = string.Empty;

        /// <summary>Bônus de vida da comida.</summary>
        public float Health;

        /// <summary>Bônus de stamina da comida.</summary>
        public float Stamina;

        /// <summary>Bônus de eitr da comida (guardado mesmo sem uso hoje).</summary>
        public float Eitr;

        /// <summary>Vida regenerada por tick de comida.</summary>
        public float Regen;

        /// <summary>Segundos de queima que ainda faltavam.</summary>
        public float Remaining;

        /// <summary>Tempo total de queima da comida.</summary>
        public float Total;
    }

    /// <summary>
    /// Uma perícia do companheiro dentro do snapshot (Fase 2.13). O <c>Skill</c> é o valor numérico
    /// de <c>Skills.SkillType</c> (guardado como <c>int</c> para o JSON não depender do enum).
    /// </summary>
    [Serializable]
    public sealed class CompanionSkillState
    {
        /// <summary><c>Skills.SkillType</c> como número (ex.: 13 = <c>WoodCutting</c>).</summary>
        public int Skill;

        /// <summary>Nível da perícia (0 a 100).</summary>
        public float Level;

        /// <summary>Progresso acumulado para o próximo nível.</summary>
        public float Accumulator;
    }
}

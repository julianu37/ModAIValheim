using ModAIValheim.Companion.Jobs;
using UnityEngine;

namespace ModAIValheim.Companion
{
    /// <summary>
    /// <b>Cuidado da base</b> (Fase 2.17): a cola entre a config <c>[Base]</c>, a casa
    /// (<see cref="CompanionHome"/>) e o trabalho de manutenção (<c>Jobs/UpkeepJob.cs</c>).
    ///
    /// <para>
    /// Aqui moram as três coisas que <b>não</b> são de nenhum dos dois:
    /// </para>
    /// <list type="number">
    ///   <item><description>o <b>modo contínuo</b> (<c>modia_basecare on|off</c>, ou
    ///   <c>Base/Continuo</c> no <c>.cfg</c>): com ele ligado o companheiro <b>retoma sozinho</b> o
    ///   cuidado da base sempre que fica ocioso (sem trabalho, sem coleta, sem depósito e sem
    ///   combate) — é o "ele cuida da casa" da visão, em vez de só rodar uma passada por comando.
    ///   O comando guarda a escolha <b>na sessão</b> (o <c>.cfg</c> continua sendo o padrão);</description></item>
    ///   <item><description>o <b>auto-início</b> desse modo, chamado pelo <see cref="CompanionAI"/>
    ///   quando não há trabalho no comando (com espera mínima entre tentativas — nunca por
    ///   frame);</description></item>
    ///   <item><description>a <b>linha de estado</b> para o <c>modia_stats</c>.</description></item>
    /// </list>
    ///
    /// <para>
    /// Adaptado (lógica) do "action mode" de manutenção de <c>src/Companions/AI/CompanionAI.cs</c>
    /// (o modo <c>ModeRepairBuildings</c> ligado no ZDO e o <c>IdleController</c> que dá pequenas
    /// tarefas de manutenção ao companheiro ocioso — <c>UpkeepController.StartIdleChore</c>) do
    /// <i>Offline Companions Reborn</i> (MIT — ver <c>THIRD_PARTY_NOTICES.md</c>): lá o modo vive no
    /// ZDO e o companheiro ocioso pega uma tarefa por vez; aqui o modo contínuo é uma opção de
    /// config/comando que <b>começa o trabalho normal</b> na fila única, sem cópia de arquivo.
    /// </para>
    /// </summary>
    internal static class CompanionBaseCare
    {
        /// <summary>Espera (s) entre duas tentativas de auto-início (evita repetir a varredura por tick).</summary>
        private const float AutoStartRetrySeconds = 10f;

        private static float _nextAutoStart = -999f;

        /// <summary>
        /// Escolha do jogador <b>nesta sessão</b> para o modo contínuo: <c>0</c> = usar a config,
        /// <c>1</c> = ligado à força, <c>-1</c> = desligado à força (o <c>modia_basecare</c>).
        /// </summary>
        private static int _continuousOverride;

        /// <summary>Cuidado da base ligado na config (<c>Base/Ligado</c>).</summary>
        internal static bool Enabled => CompanionBaseCareConfig.Enabled;

        /// <summary>
        /// Modo contínuo efetivo: a escolha da sessão (<c>modia_basecare</c>) quando existe, senão a
        /// config (<c>Base/Continuo</c>).
        /// </summary>
        internal static bool Continuous =>
            _continuousOverride != 0 ? _continuousOverride > 0 : CompanionBaseCareConfig.Continuous;

        /// <summary>True quando o modo contínuo foi forçado por comando nesta sessão.</summary>
        internal static bool Overridden => _continuousOverride != 0;

        /// <summary>Liga/desliga o modo contínuo nesta sessão (comando <c>modia_basecare</c>).</summary>
        internal static void SetContinuous(bool value, string source)
        {
            _continuousOverride = value ? 1 : -1;

            // Uma tentativa de auto-início pode sair já no próximo tick da IA.
            _nextAutoStart = -999f;

            Plugin.Log.LogInfo(
                $"[ModIA] Base: modo continuo {(value ? "LIGADO" : "DESLIGADO")} nesta sessao " +
                $"(por {source}; o padrao do .cfg e Base/Continuo = {CompanionBaseCareConfig.Continuous}).");
        }

        /// <summary>
        /// Tenta começar o cuidado da base <b>sozinho</b> (modo contínuo). Chamado pelo
        /// <see cref="CompanionAI"/> quando não há trabalho no comando.
        ///
        /// <para>
        /// Só age quando o companheiro está realmente livre (sem alvo de combate, sem coleta, sem
        /// viagem ao baú e sem loot pendente) e existe uma casa definida — o modo contínuo trabalha
        /// <b>em casa</b>, senão o NPC sairia cuidando do mundo inteiro. Devolve <c>true</c> quando
        /// o trabalho foi pedido.
        /// </para>
        /// </summary>
        internal static bool TryAutoStart(CompanionAI companion)
        {
            if (!Enabled || !Continuous || companion == null)
            {
                return false;
            }

            if (!CompanionJobsConfig.Enabled || !CompanionBaseCareConfig.AnyTask)
            {
                return false;
            }

            // Modo contínuo é "cuidar da casa": sem casa definida não há onde trabalhar.
            if (!CompanionHome.IsSet)
            {
                return false;
            }

            if (Time.time < _nextAutoStart)
            {
                return false;
            }

            if (!companion.IsLocallyOwned)
            {
                return false;
            }

            if (companion.GetTargetCreature() != null ||
                CompanionHarvest.IsActive ||
                CompanionDeposit.TripActive ||
                CompanionLoot.HasWork)
            {
                return false;
            }

            _nextAutoStart = Time.time + AutoStartRetrySeconds;

            if (CompanionJobQueue.Request(
                    new UpkeepJob { Continuous = true },
                    companion,
                    0,
                    "modo continuo (Base/Continuo)",
                    out string error))
            {
                return true;
            }

            Plugin.Log.LogInfo($"[ModIA] Base: modo continuo nao conseguiu comecar ({error}).");
            return false;
        }

        /// <summary>
        /// Encerra o cuidado da base contínuo (comando <c>modia_basecare off</c>): só cancela se o
        /// trabalho no comando for o de manutenção em modo contínuo — um <c>upkeep</c> pedido à mão
        /// (ou qualquer outro trabalho) não é mexido.
        /// </summary>
        internal static bool CancelContinuous(CompanionAI companion, string reason)
        {
            CompanionJob job = CompanionJobQueue.Current;
            if (job is UpkeepJob upkeep && upkeep.Continuous)
            {
                CompanionJobQueue.Cancel(companion, reason);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Um comando explícito de parada (<c>modia_stop</c>/<c>modia_job stop</c>) <b>desliga</b> o
        /// modo contínuo nesta sessão: "parar" tem de parar de verdade — sem isto o companheiro
        /// voltaria a cuidar da casa sozinho segundos depois e o comando pareceria não funcionar.
        /// Devolve <c>true</c> quando o modo contínuo foi desligado (para o comando avisar).
        /// </summary>
        internal static bool SuspendForStopCommand(string source)
        {
            if (!Continuous)
            {
                return false;
            }

            SetContinuous(false, source);
            return true;
        }

        /// <summary>Linha de estado do cuidado da base para o <c>modia_stats</c>.</summary>
        internal static string Describe()
        {
            string tasks = DescribeTasks();
            string continuous = Continuous
                ? (Overridden ? "continuo (nesta sessao)" : "continuo")
                : "so por comando";

            return $"ligado = {Enabled}, {continuous}, tarefas: {tasks}";
        }

        /// <summary>Quais tarefas de cuidado estão ligadas (linha curta, na ordem de prioridade).</summary>
        private static string DescribeTasks()
        {
            if (!CompanionBaseCareConfig.AnyTask)
            {
                return "nenhuma (veja Base/* no .cfg)";
            }

            string text = string.Empty;
            if (CompanionBaseCareConfig.Repair)
            {
                text += "reparar";
            }

            if (CompanionBaseCareConfig.Refuel)
            {
                text = Append(text, "abastecer");
            }

            if (CompanionBaseCareConfig.Hives)
            {
                text = Append(text, "colmeias");
            }

            if (CompanionBaseCareConfig.SortChests)
            {
                text = Append(text, "ordenar baus");
            }

            if (CompanionBaseCareConfig.MergeStacks)
            {
                text = Append(text, "juntar pilhas");
            }

            return text;
        }

        private static string Append(string text, string value)
        {
            return text.Length == 0 ? value : text + ", " + value;
        }

        /// <summary>Solta o estado estático (unload do plugin / fim do mundo).</summary>
        internal static void Shutdown()
        {
            _nextAutoStart = -999f;
            _continuousOverride = 0;
        }
    }
}

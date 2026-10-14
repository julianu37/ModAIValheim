using UnityEngine;

namespace ModAIValheim.Companion.Jobs
{
    /// <summary>
    /// <b>Fila única de trabalho</b> do companheiro (Fase 2.16).
    ///
    /// <para>
    /// Existe <b>um</b> trabalho ativo por vez e é esta fila quem o possui: o jogador aponta
    /// (<c>modia_job hunt|fish</c>), o companheiro executa de ponta a ponta e, quando ele termina
    /// (meta atingida, fauna esgotada, tempo esgotado…), a fila fecha a conta (HUD + log) e o
    /// companheiro volta a seguir o jogador. Um comando novo <b>substitui</b> o trabalho em
    /// andamento — a intenção do jogador é explícita e imediata (o trabalho antigo é encerrado com o
    /// motivo "novo trabalho pedido", nunca fica pendurado).
    /// </para>
    ///
    /// <para>
    /// <b>Prioridade explícita</b> (a mesma ordem das camadas do
    /// <see cref="CompanionAI.UpdateAI"/>, todas com o "dono único do movimento" por frame):
    /// </para>
    /// <list type="number">
    ///   <item><description><b>combate</b> — alvo hostil: o motor nativo persegue/ataca (o caçar
    ///   <i>vive</i> de combate: quem engaja o alvo é o próprio trabalho).</description></item>
    ///   <item><description><b>coleta</b> — <c>CompanionHarvest.IsActive</c> pausa o trabalho (ele
    ///   espera, sem contar no relógio).</description></item>
    ///   <item><description><b>depósito</b> — viagem ao baú (<c>CompanionDeposit.TripActive</c>)
    ///   também pausa o trabalho.</description></item>
    ///   <item><description><b>loot</b> — <c>CompanionLoot</c>: o caçador espera catar o abate antes
    ///   de sair caçando de novo; o pescador espera parado (e recolhe a linha) enquanto o companheiro
    ///   vai ao corpo.</description></item>
    ///   <item><description><b>trabalho</b> — a camada <c>UpdateJob</c> (este tipo), com
    ///   <see cref="MovementOwner.Hunt"/>/<see cref="MovementOwner.Fish"/>.</description></item>
    ///   <item><description><b>follow</b> — o motor nativo (o trabalho devolve o alvo de follow
    ///   quando acaba).</description></item>
    /// </list>
    ///
    /// <para>
    /// Adaptado (lógica) da ordem de tarefas de <c>src/Companions/AI/CompanionAI.cs</c> e de
    /// <c>src/Companions/Jobs/MHR_JobRules.cs</c> do <i>Offline Companions Reborn</i> (MIT — ver
    /// <c>THIRD_PARTY_NOTICES.md</c>): lá o "action mode" do ZDO escolhe <b>um</b> controlador de
    /// trabalho e as tarefas rodam numa única passagem (loot → trabalho → follow). Aqui a escolha é
    /// do jogador (comando de console) e o dono da ordem é esta fila estática.
    /// </para>
    /// </summary>
    internal static class CompanionJobQueue
    {
        private static CompanionJob _current;

        /// <summary>Trabalho no comando (null quando não há nenhum).</summary>
        internal static CompanionJob Current => _current;

        /// <summary>True quando existe um trabalho ativo.</summary>
        internal static bool HasJob => _current != null && _current.IsActive;

        /// <summary>True quando o trabalho está de fato rodando (não terminando).</summary>
        internal static bool IsRunning => _current != null && _current.State == JobState.Running;

        /// <summary>Nome do trabalho atual para o HUD/console (null quando não há).</summary>
        internal static string CurrentName => HasJob ? _current.DisplayName : null;

        /// <summary>
        /// Pede um trabalho novo. Substitui o que estiver em andamento (motivo "novo trabalho
        /// pedido"). Devolve <c>false</c> com <paramref name="error"/> quando não dá para começar.
        /// </summary>
        internal static bool Request(
            CompanionJob job,
            CompanionAI companion,
            int amount,
            string source,
            out string error)
        {
            error = null;

            if (job == null)
            {
                error = "[ModIA] Trabalho desconhecido.";
                return false;
            }

            if (!CompanionJobsConfig.Enabled)
            {
                error = "[ModIA] Os trabalhos estao desligados (Jobs/Ligado = false no .cfg).";
                return false;
            }

            if (companion == null)
            {
                error = "[ModIA] Nenhum companheiro no mundo (spawne com a tecla configurada).";
                return false;
            }

            if (!companion.IsLocallyOwned)
            {
                // A IA roda no dono do ZDO: num cliente sem autoridade o trabalho não anda.
                Plugin.Log.LogWarning(
                    "[ModIA] Sem autoridade sobre o companheiro (cliente): o trabalho só roda no " +
                    "host/dono do ZDO.");
            }

            Cancel(companion, "novo trabalho pedido");

            if (!job.Start(companion, amount, out error))
            {
                return false;
            }

            _current = job;

            string limit = job.Amount > 0 ? $"{job.Amount}x" : "sem limite";
            Plugin.Log.LogInfo(
                $"[ModIA] Trabalho iniciado: {job.DisplayName} ({limit}) por {source}.");
            CompanionJob.ShowHud($"Trabalho: {job.DisplayName} ({limit})");
            return true;
        }

        /// <summary>
        /// Encerra o trabalho atual (se houver) devolvendo o companheiro ao estado normal. Não
        /// avisa no HUD: quem chama decide a mensagem (comando, dispensa, morte…).
        /// </summary>
        internal static void Cancel(CompanionAI companion, string reason)
        {
            if (_current == null)
            {
                return;
            }

            CompanionJob job = _current;
            _current = null;
            job.Stop(companion, reason);

            Plugin.Log.LogInfo(
                $"[ModIA] Trabalho {job.DisplayName} interrompido ({reason}): " +
                $"{job.Progress} feito(s) em {job.Elapsed / 60f:0.#} min.");
        }

        /// <summary>
        /// Avança o trabalho atual: teto de tempo, um passo do controlador e, quando ele termina, a
        /// conta final (log + HUD) com o companheiro devolvido ao follow.
        /// </summary>
        internal static void Tick(CompanionAI companion, float dt)
        {
            if (_current == null)
            {
                return;
            }

            CompanionJob job = _current;

            float maxSeconds = CompanionJobsConfig.MaxSeconds;
            if (job.State == JobState.Running && maxSeconds > 0f && job.Elapsed >= maxSeconds)
            {
                Plugin.Log.LogInfo(
                    $"[ModIA] Trabalho {job.DisplayName}: tempo maximo de {maxSeconds / 60f:0.#} min " +
                    "atingido; encerrando.");
                job.Stop(companion, $"tempo maximo ({maxSeconds / 60f:0.#} min)");
                Finish(job);
                return;
            }

            job.Tick(companion, dt);

            if (job.State != JobState.Running)
            {
                Finish(job);
            }
        }

        /// <summary>Fecha a conta de um trabalho que terminou (HUD + log) e larga a referência.</summary>
        private static void Finish(CompanionJob job)
        {
            if (_current == job)
            {
                _current = null;
            }

            string reason = job.StopReason ?? "encerrado";
            string done = job.Amount > 0 ? $"{job.Progress}/{job.Amount}" : $"{job.Progress}";

            Plugin.Log.LogInfo(
                $"[ModIA] Trabalho {job.DisplayName} encerrado ({reason}): {done} em " +
                $"{job.Elapsed / 60f:0.#} min.");
            CompanionJob.ShowHud($"{job.DisplayName} encerrado: {done} ({reason})");
        }

        /// <summary>
        /// Linha de estado para o <c>modia_stats</c>/menu: o trabalho, a quantidade e o que ele está
        /// fazendo agora.
        /// </summary>
        internal static string Describe()
        {
            CompanionJob job = _current;
            if (job == null || !job.IsActive)
            {
                return "nenhum (use modia_job hunt|fish)";
            }

            string limit = job.Amount > 0 ? $"{job.Progress}/{job.Amount}" : $"{job.Progress}";
            string status = string.IsNullOrEmpty(job.Status) ? "" : $" — {job.Status}";
            return $"{job.DisplayName} ({limit}, {job.Elapsed / 60f:0.#} min){status}";
        }

        /// <summary>Solta o estado estático (unload do plugin / fim do mundo).</summary>
        internal static void Shutdown()
        {
            // Sem companheiro não há trabalho: nada de chamar OnStop (os GameObjects podem já estar
            // destruídos no shutdown do mundo).
            _current = null;
            CompanionJob.ResetLogState();
        }
    }
}

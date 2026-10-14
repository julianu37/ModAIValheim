using UnityEngine;

namespace ModAIValheim.Companion.Jobs
{
    /// <summary>
    /// Tipo de <b>trabalho</b> (Fase 2.16). Caçar/pescar (2.16a) e cultivar/cozinhar/fundir (2.16b),
    /// cada um com o seu controlador na mesma fila.
    /// </summary>
    internal enum JobKind
    {
        /// <summary>Nenhum trabalho (companheiro livre para seguir o jogador).</summary>
        None = 0,

        /// <summary>Caçar fauna (abater e catar o que caiu).</summary>
        Hunt = 1,

        /// <summary>Pescar com vara numa margem próxima.</summary>
        Fish = 2,

        /// <summary>Cultivar numa zona de fazenda (plantar, colher e guardar a colheita).</summary>
        Farm = 3,

        /// <summary>Cozinhar/bebidas (abastecer, esperar e retirar o que ficou pronto).</summary>
        Cook = 4,

        /// <summary>Operar fundições (abastecer minério/combustível e retirar o produto).</summary>
        Smelt = 5,
    }

    /// <summary>
    /// Estado do trabalho na <see cref="CompanionJobQueue"/>.
    /// </summary>
    internal enum JobState
    {
        /// <summary>Parado (nunca começou ou já foi encerrado).</summary>
        Idle = 0,

        /// <summary>Em execução (é ele quem dirige o companheiro).</summary>
        Running = 1,

        /// <summary>Terminou sozinho (meta atingida, fauna esgotada, sem peixe…).</summary>
        Done = 2,
    }

    /// <summary>
    /// Base dos <b>trabalhos</b> do companheiro (Fase 2.16): o "aponte para o trabalho" do
    /// <c>docs/vision.md</c> — o jogador aponta e ele executa de ponta a ponta.
    ///
    /// <para>
    /// Cada trabalho é uma <b>máquina de estados própria</b> (em arquivo separado), sem
    /// <c>MonoBehaviour</c> e sem varredura por frame: o <see cref="CompanionAI"/> chama
    /// <see cref="Tick"/> na camada <c>UpdateJob</c> (~20 Hz, junto das demais camadas) e o
    /// controlador só faz trabalho pesado com intervalo próprio (varredura de alvo, espera de
    /// fisgada, etc.), com buffers reaproveitados.
    /// </para>
    ///
    /// <para>
    /// <b>Contrato:</b> <see cref="OnStart"/> valida e prepara (devolve <c>false</c> + motivo quando
    /// não dá para começar), <see cref="OnTick"/> avança um passo e <see cref="OnStop"/> devolve o
    /// companheiro ao estado normal (largar alvo, guardar a vara, parar de andar). O trabalho
    /// <b>nunca</b> chama <c>MoveTo</c>/<c>LookAt</c> direto: usa a superfície
    /// <c>CompanionAI.Job*</c>, que reivindica o <see cref="MovementOwner"/> do frame — é o mesmo
    /// "dono único do movimento" da Fase 2.10, agora com <see cref="MovementOwner.Hunt"/> e
    /// <see cref="MovementOwner.Fish"/>.
    /// </para>
    ///
    /// <para>
    /// Adaptado (lógica) de <c>src/Companions/Jobs/HuntController.cs</c> e
    /// <c>src/Companions/Jobs/FishController.cs</c> do <i>Offline Companions Reborn</i> (MIT — ver
    /// <c>THIRD_PARTY_NOTICES.md</c>): a referência usa um controlador por trabalho atrás de um
    /// "action mode" no ZDO e ordena as tarefas numa única passagem de <c>UpdateAI</c>
    /// (<c>loot</c> → trabalhos → <c>follow</c>); aqui a ordem é a mesma, com a fila única
    /// <see cref="CompanionJobQueue"/> e a prioridade explícita
    /// <b>combate &gt; coleta &gt; depósito &gt; loot &gt; trabalho &gt; follow</b>.
    /// </para>
    /// </summary>
    internal abstract class CompanionJob
    {
        /// <summary>Janela (s) em que a mesma chave de log não se repete.</summary>
        private const float LogRepeatSeconds = 10f;

        /// <summary>Intervalo (s) mínimo entre dois pedidos de viagem ao baú por mochila cheia.</summary>
        private const float PackFullRetrySeconds = 5f;

        private static string s_lastLogKey;
        private static float s_lastLogAt = -999f;

        /// <summary>Espera até o próximo pedido de viagem ao baú (evita varrer baús todo tick).</summary>
        private float _packFullTimer;

        /// <summary>Tipo do trabalho (usado pelos comandos e pelo HUD).</summary>
        internal abstract JobKind Kind { get; }

        /// <summary>Nome em português para o HUD/console (ex.: <c>Caçar</c>).</summary>
        internal abstract string DisplayName { get; }

        /// <summary>Nome curto usado no comando de console (ex.: <c>hunt</c>).</summary>
        internal abstract string CommandName { get; }

        /// <summary>Estado atual na fila.</summary>
        internal JobState State { get; private set; }

        /// <summary>Quantidade alvo (0 = sem limite: trabalha até acabar o que há no raio).</summary>
        internal int Amount { get; private set; }

        /// <summary>
        /// Quanto já foi feito: abates, peixes na mão, culturas colhidas/plantadas, pratos feitos,
        /// produtos catados da fornalha… (o que cada trabalho conta é decisão dele).
        /// </summary>
        internal int Progress { get; private set; }

        /// <summary>Segundos desde o começo (congelado enquanto o trabalho espera a vez).</summary>
        internal float Elapsed { get; private set; }

        /// <summary>O que o companheiro está fazendo agora (linha curta para o HUD/console).</summary>
        internal string Status { get; private set; }

        /// <summary>Por que o trabalho terminou (null enquanto roda).</summary>
        internal string StopReason { get; private set; }

        /// <summary>True enquanto o trabalho está no comando (rodando ou terminando).</summary>
        internal bool IsActive => State != JobState.Idle;

        /// <summary>True quando a meta de quantidade foi atingida.</summary>
        internal bool AmountReached => Amount > 0 && Progress >= Amount;

        /// <summary>
        /// Começa o trabalho. Devolve <c>false</c> com <paramref name="error"/> preenchido quando não
        /// é possível começar (sem ferramenta, modo passivo, sem autoridade sobre o companheiro…).
        /// </summary>
        internal bool Start(CompanionAI companion, int amount, out string error)
        {
            error = null;
            Amount = Mathf.Max(0, amount);
            Progress = 0;
            Elapsed = 0f;
            StopReason = null;
            Status = "começando";
            _packFullTimer = 0f;

            if (!OnStart(companion, out error))
            {
                State = JobState.Idle;
                return false;
            }

            State = JobState.Running;
            return true;
        }

        /// <summary>
        /// Avança um passo do trabalho (chamado pelo <see cref="CompanionJobQueue"/> no tick da IA).
        /// O relógio (<see cref="Elapsed"/>) só anda quando o trabalho de fato roda — enquanto ele
        /// espera a vez (coleta/depósito no comando) nada conta contra o teto de tempo.
        /// </summary>
        internal void Tick(CompanionAI companion, float dt)
        {
            if (State != JobState.Running)
            {
                return;
            }

            Elapsed += dt;
            OnTick(companion, dt);
        }

        /// <summary>
        /// Encerra o trabalho por decisão de fora (comando <c>modia_job stop</c>, dispensa do
        /// companheiro, tempo esgotado…). Idempotente.
        /// </summary>
        internal void Stop(CompanionAI companion, string reason)
        {
            if (!IsActive)
            {
                return;
            }

            State = JobState.Idle;
            StopReason = reason;
            OnStop(companion, reason);
        }

        /// <summary>
        /// O trabalho decide que <b>acabou</b> (meta atingida, fauna esgotada, sem vara…). O
        /// <see cref="CompanionJobQueue"/> fecha a conta no mesmo tick (HUD + log) e larga a
        /// referência.
        /// </summary>
        protected void Finish(CompanionAI companion, string reason)
        {
            if (State != JobState.Running)
            {
                return;
            }

            State = JobState.Done;
            StopReason = reason;
            OnStop(companion, reason);
        }

        /// <summary>Conta um item concluído do trabalho (um abate, um peixe na mão).</summary>
        protected void AddProgress()
        {
            Progress++;
        }

        /// <summary>Conta <paramref name="count"/> itens concluídos de uma vez (uma colheita inteira).</summary>
        protected void AddProgress(int count)
        {
            if (count > 0)
            {
                Progress += count;
            }
        }

        /// <summary>
        /// A mochila não aceita mais nada: pede a viagem ao baú da Fase 2.14 (que assume o
        /// movimento e pausa o trabalho) ou avisa no HUD, <b>no máximo a cada
        /// <see cref="PackFullRetrySeconds"/></b> — sem esse limite um trabalho de estação varreria
        /// baús a cada tick de coleta.
        /// </summary>
        protected void ReportPackFull(CompanionAI companion, float dt, ItemDrop.ItemData item = null)
        {
            _packFullTimer -= dt;
            if (_packFullTimer > 0f)
            {
                return;
            }

            _packFullTimer = PackFullRetrySeconds;
            companion.JobReportPackFull(item);
        }

        /// <summary>Atualiza a linha de estado mostrada no HUD/console.</summary>
        protected void SetStatus(string status)
        {
            Status = status;
        }

        /// <summary>Prepara o trabalho. <paramref name="error"/> explica a recusa (HUD/console).</summary>
        protected abstract bool OnStart(CompanionAI companion, out string error);

        /// <summary>Um passo do trabalho (já com o companheiro disponível).</summary>
        protected abstract void OnTick(CompanionAI companion, float dt);

        /// <summary>Devolve o companheiro ao estado normal (largar alvo, guardar a ferramenta, parar).</summary>
        protected abstract void OnStop(CompanionAI companion, string reason);

        // ---------------------------------------------------------------------
        // Apoio (log deduplicado e HUD — o mesmo padrão dos outros sistemas)
        // ---------------------------------------------------------------------

        /// <summary>
        /// Loga um evento do trabalho <b>somente quando ele muda</b>: a mesma chave não sai de novo
        /// dentro de <see cref="LogRepeatSeconds"/> (é o que impede o log de repetir a mesma linha a
        /// cada tick, como no log de nós da coleta).
        /// </summary>
        protected static void LogEvent(string key, string message)
        {
            if (key != null && key == s_lastLogKey && Time.time - s_lastLogAt < LogRepeatSeconds)
            {
                return;
            }

            s_lastLogKey = key;
            s_lastLogAt = Time.time;
            Plugin.Log.LogInfo(message);
        }

        /// <summary>Mensagem no HUD (canto superior esquerdo), como a coleta e o depósito já fazem.</summary>
        internal static void ShowHud(string text)
        {
            CompanionHarvest.ShowMessage(text, MessageHud.MessageType.TopLeft);
        }

        /// <summary>
        /// Linha de progresso no HUD, no mesmo formato do caçar/pescar:
        /// <c>Rótulo: item (feitos/total)</c> — o <c>/total</c> só aparece quando há meta.
        /// </summary>
        protected void ShowProgress(string label, string item)
        {
            string limit = Amount > 0 ? $"/{Amount}" : string.Empty;
            ShowHud($"{label}: {item} ({Progress}{limit})");
        }

        /// <summary>Solta o estado estático de log (unload do plugin / fim do mundo).</summary>
        internal static void ResetLogState()
        {
            s_lastLogKey = null;
            s_lastLogAt = -999f;
        }
    }
}

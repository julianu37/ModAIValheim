using System;
using HarmonyLib;

namespace ModAIValheim.Companion
{
    /// <summary>
    /// Patch Harmony de persistência do companheiro (Fase 2.7).
    ///
    /// <para>
    /// <c>ZNetScene.Shutdown()</c> é o ponto por onde o jogo passa <b>saindo do mundo</b>
    /// (<c>Game.Shutdown</c> → <c>ZNetScene.instance.Shutdown()</c>) e <b>fechando o jogo</b>
    /// (<c>Game.OnApplicationQuit</c> → <c>Shutdown</c>). É ele que destrói as instâncias do
    /// mundo — inclusive o companheiro — então um <b>prefix</b> aqui roda enquanto o NPC ainda
    /// existe e o inventário dele ainda está inteiro.
    /// </para>
    ///
    /// <para>
    /// Sem isso, quem fecha o jogo com o companheiro vivo (sem dispensar) perderia os itens dele
    /// (o prefab é não-persistente: nada dele vai para o save do mundo). O patch é
    /// <b>somente observador</b>: captura o snapshot e não altera argumentos nem o resultado. Todo
    /// o corpo está em <c>try/catch</c> de propósito — uma exceção em prefix impediria o
    /// <c>Shutdown</c> original de rodar (e o mundo não fecharia direito).
    /// </para>
    /// </summary>
    internal static class CompanionStatePatches
    {
        [HarmonyPatch(typeof(ZNetScene), nameof(ZNetScene.Shutdown))]
        internal static class ZNetScene_Shutdown_Patch
        {
            private static void Prefix()
            {
                try
                {
                    if (!CompanionStateStore.PersistBetweenSessions)
                    {
                        // Sem persistência em disco o estado na memória morre com o processo:
                        // não vale a pena varrer o mundo à toa.
                        return;
                    }

                    CompanionStateStore.CaptureActive("fim do mundo");
                }
                catch (Exception e)
                {
                    Plugin.Log.LogWarning(
                        $"[ModIA] Falha ao capturar o companheiro no fim do mundo: {e.Message}");
                }
            }
        }
    }
}

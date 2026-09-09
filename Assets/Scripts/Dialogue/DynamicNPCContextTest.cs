using UnityEngine;

namespace Detective.Dialogue
{
    // Diferente do AzureConnectionTest (so valida que a API responde), este
    // teste valida que o modelo esta REALMENTE usando o contexto injetado:
    // a persona, as cartas do inventario e o historico da conversa entre
    // turnos. Usa o DynamicNPCController de verdade - o mesmo caminho que
    // roda no jogo, nao uma chamada isolada.
    //
    // Uso:
    // 1. No GameObject com DynamicNPCController, preencha "clueCardNames" com
    //    um item INVENTADO e inconfundivel (algo que o modelo nao teria
    //    como "chutar" por conhecimento geral, ex: "Estatueta de bronze com
    //    a inscricao XZ-19"). Isso e o que prova que a resposta veio do
    //    prompt, e nao de coincidencia.
    // 2. Arraste esse DynamicNPCController pro campo "Controller" abaixo.
    // 3. Play Mode -> botao direito no cabecalho -> "Testar Contexto".
    // 4. Leia o Console: a "pista-isca" deve aparecer nas falas do turno 2
    //    e ser referenciada de novo (com outras palavras) no turno 3.
    public class DynamicNPCContextTest : MonoBehaviour
    {
        public DynamicNPCController controller;

        [ContextMenu("Testar Contexto (Persona + Pistas + Memória)")]
        public async void TestContext()
        {
            if (controller == null)
            {
                Debug.LogError("[DynamicNPCContextTest] Campo 'controller' não atribuído.");
                return;
            }

            Debug.Log("[DynamicNPCContextTest] ── Turno 1: abertura espontânea (valida a persona) ──");
            DynamicTurnResult turn1 = await controller.StartConversation();
            LogTurn(1, turn1);

            Debug.Log("[DynamicNPCContextTest] ── Turno 2: pergunta direta sobre pistas (valida injeção do inventário) ──");
            DynamicTurnResult turn2 = await controller.ChooseOption(
                "Você tem alguma pista específica sobre o crime que possa compartilhar comigo?");
            LogTurn(2, turn2);

            Debug.Log("[DynamicNPCContextTest] ── Turno 3: pede pra repetir (valida memória da conversa) ──");
            DynamicTurnResult turn3 = await controller.ChooseOption(
                "Desculpe, não entendi direito. Pode repetir o que você acabou de dizer, com outras palavras?");
            LogTurn(3, turn3);

            Debug.Log(
                "[DynamicNPCContextTest] Teste concluído. Confira manualmente: " +
                "(a) o tom do turno 1 bate com a persona configurada; " +
                "(b) a pista-isca do inventário aparece no turno 2; " +
                "(c) o turno 3 é uma reformulação coerente do turno 2, não algo novo/contraditório.");
        }

        private void LogTurn(int n, DynamicTurnResult result)
        {
            Debug.Log($"[DynamicNPCContextTest] Turno {n} · fala: {result.fala}");
            Debug.Log($"[DynamicNPCContextTest] Turno {n} · opções: {string.Join(" | ", result.opcoes)}");
            Debug.Log($"[DynamicNPCContextTest] Turno {n} · encerrar: {result.encerrar}");
        }
    }
}

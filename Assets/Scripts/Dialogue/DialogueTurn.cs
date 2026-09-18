using System;

namespace Detective.Dialogue
{
    // Um "turno" de conversa, ja normalizado: e isto que a interface exibe,
    // independentemente de a fala ter vindo de um arquivo .yarn escrito a mao
    // ou do modelo de linguagem na Azure. Esse e o ponto-chave do experimento -
    // se as duas origens produzem exatamente a mesma estrutura de dados, a
    // interface nao tem como (nem por acidente) revelar qual e qual.
    public class DialogueTurn
    {
        public string speakerName;
        public string line;
        public string[] options;

        // true quando a conversa acabou (no terminal do .yarn, ou "encerrar"
        // do modelo). A interface mostra um botao de encerrar em vez de opcoes.
        public bool isEnd;
    }

    // Contrato comum das duas origens de dialogo. A interface unificada fala
    // so com isto, nunca diretamente com o Yarn ou com o cliente da Azure.
    public interface IDialogueSource
    {
        // Nome exibido do personagem (o mesmo para as duas origens).
        string SpeakerName { get; }

        // Quando true, a interface aplica um atraso artificial antes de
        // revelar a fala. So as falas pre-escritas (Yarn) precisam disso: o
        // NPC dinamico ja demora naturalmente o tempo da chamada de rede.
        bool NeedsArtificialDelay { get; }

        // Dispara a conversa. O callback e chamado quando o turno esta pronto.
        void Begin(Action<DialogueTurn> onTurnReady);

        // O jogador escolheu a opcao de indice optionIndex do turno atual.
        void Choose(int optionIndex);

        // Encerra a conversa (jogador saiu, ou turno final).
        void End();
    }
}

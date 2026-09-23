using System.Collections.Generic;
using UnityEngine;

namespace Detective.Dialogue
{
    // Cache local de falas pre-geradas, usado quando a chamada ao Azure OpenAI
    // falha durante uma sessao de teste (timeout, erro de rede, indisponibilidade).
    //
    // Isto implementa o plano de contingencia ja registrado no TCC (secao 4.7.3,
    // risco "Problemas tecnicos durante a integracao com motores de IA" ->
    // "Usar cache local de dialogos pre-gerados"). Ver docs/arquitetura-npc-dinamico.md
    // secao 4.5.
    [CreateAssetMenu(fileName = "FallbackDialoguePool", menuName = "Detective/Fallback Dialogue Pool")]
    public class FallbackDialoguePool : ScriptableObject
    {
        [Tooltip("Falas neutras, genericas o suficiente para qualquer personagem " +
                 "(nao mencionam pistas especificas, para nao contradizer o crimeEnvelope sorteado).")]
        [TextArea(2, 4)]
        public List<string> genericLines = new List<string>
        {
            "Prefiro pensar um pouco antes de responder isso, detetive.",
            "Essa e uma pergunta dificil... me de um momento.",
            "Nao tenho certeza do que dizer sobre isso agora.",
        };

        [Tooltip("Opcoes genericas oferecidas junto com a fala de fallback. " +
                 "O tamanho desta lista deve bater com AzureOpenAIConfig.optionCount.")]
        public List<string> genericOptions = new List<string>
        {
            "Podemos voltar a isso depois.",
            "Vou continuar investigando outras pistas.",
        };

        [TextArea(2, 4)]
        public string closingLine = "Acho que já disse tudo que posso, por ora. Volte se precisar de algo mais.";

        public DynamicTurnResult BuildFallbackTurn(bool forceClose)
        {
            string line = forceClose
                ? closingLine
                : genericLines[Random.Range(0, genericLines.Count)];

            // As opcoes genericas ("voltamos a isso depois"...) sao saidas: se o
            // jogador escolher uma, o turno seguinte e uma despedida, como no
            // no Encerrar dos roteirizados.
            var roles = new string[genericOptions.Count];
            for (int i = 0; i < roles.Length; i++)
                roles[i] = OptionRoles.Saida;

            return new DynamicTurnResult
            {
                fala = line,
                opcoes = genericOptions.ToArray(),
                papeis_opcoes = roles,
                encerrar = forceClose,
                revelar_pista = "",
                carta_alvo = ""
            };
        }
    }
}

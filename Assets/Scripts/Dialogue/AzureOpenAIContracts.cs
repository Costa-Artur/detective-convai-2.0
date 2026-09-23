using System;

namespace Detective.Dialogue
{
    // Estruturas de dados usadas na chamada HTTP ao Azure OpenAI e no contrato de
    // saída do NPC dinâmico. Ver docs/arquitetura-npc-dinamico.md secoes 4.1 e 4.2.

    [Serializable]
    public class ChatMessage
    {
        public string role;    // "system" | "user" | "assistant"
        public string content;

        public ChatMessage(string role, string content)
        {
            this.role = role;
            this.content = content;
        }
    }

    [Serializable]
    internal class SchemaField
    {
        public string type;
    }

    [Serializable]
    internal class SchemaArrayField
    {
        public string type = "array";
        public SchemaField items = new SchemaField { type = "string" };
        public int minItems;
        public int maxItems;
    }

    // String restrita a uma lista de valores ("enum" e palavra reservada em
    // C#; o @ faz o JsonUtility gravar o campo como "enum").
    [Serializable]
    internal class SchemaEnumField
    {
        public string type = "string";
        public string[] @enum;
    }

    [Serializable]
    internal class SchemaEnumArrayField
    {
        public string type = "array";
        public SchemaEnumField items;
        public int minItems;
        public int maxItems;
    }

    [Serializable]
    internal class TurnSchemaProperties
    {
        public SchemaField fala = new SchemaField { type = "string" };
        public SchemaArrayField opcoes;

        // Papel de cada opcao, na mesma ordem de "opcoes". E o que permite ao
        // codigo saber o que o jogador escolheu (pedir a carta, pressionar,
        // sair...) e conduzir o turno seguinte pelo mesmo roteiro dos NPCs
        // roteirizados, em vez de depender do modelo interpretar o texto.
        public SchemaEnumArrayField papeis_opcoes;

        public SchemaField encerrar = new SchemaField { type = "boolean" };

        // Equivalente ao comando <<reveal>> dos .yarn: e assim que o NPC
        // dinamico ENTREGA uma carta ao jogador, e nao apenas fala sobre ela.
        // Valor = NOME exato de uma carta do NPC (o enum e montado a cada
        // requisicao com as cartas dele), ou "" para nao revelar. Revelar pelo
        // nome - e nao pelo tipo - garante que a carta mostrada e a que o
        // modelo escolheu. Nao e opcional porque o modo estrito exige que toda
        // propriedade seja obrigatoria - dai o "" em vez de null.
        public SchemaEnumField revelar_pista;

        // Carta a que a opcao de papel "pedido_carta" se refere (o indicio
        // deixado neste turno), ou "". Usada para entregar a carta certa no
        // turno seguinte.
        public SchemaEnumField carta_alvo;
    }

    [Serializable]
    internal class TurnSchema
    {
        public string type = "object";
        public TurnSchemaProperties properties;
        public string[] required = { "fala", "opcoes", "papeis_opcoes", "encerrar", "revelar_pista", "carta_alvo" };
        public bool additionalProperties = false;
    }

    [Serializable]
    internal class JsonSchemaWrapper
    {
        public string name = "npc_turn";
        public bool strict = true;
        public TurnSchema schema;
    }

    [Serializable]
    internal class ResponseFormat
    {
        public string type = "json_schema";
        public JsonSchemaWrapper json_schema;
    }

    // Corpo de request para modelos NAO-reasoning (ex: gpt-4.1-mini) via Chat
    // Completions (API v1) - aceita max_tokens/temperature. "model" leva o
    // nome do deployment - na API v1 ele nao vai mais na URL.
    [Serializable]
    internal class ChatRequest
    {
        public string model;
        public ChatMessage[] messages;
        public ResponseFormat response_format;
        public int max_tokens;
        public float temperature;
    }

    // Corpo de request para modelos de RACIOCINIO (familia gpt-5.x, ex:
    // gpt-5.6-luna) via Chat Completions (API v1) - usa max_completion_tokens
    // e reasoning_effort; NAO tem campo temperature (JsonUtility serializa
    // todo campo presente na classe, entao precisa ser um DTO separado, nao
    // dava pra so deixar temperature em zero).
    [Serializable]
    internal class ReasoningChatRequest
    {
        public string model;
        public ChatMessage[] messages;
        public ResponseFormat response_format;
        public int max_completion_tokens;
        public string reasoning_effort;
    }

    [Serializable]
    internal class ChatResponseMessage
    {
        public string role;
        public string content;
    }

    [Serializable]
    internal class ChatChoice
    {
        public ChatResponseMessage message;
        public string finish_reason;
    }

    [Serializable]
    internal class ChatResponse
    {
        public ChatChoice[] choices;
    }

    // Contrato de saída de um turno do NPC dinâmico. Ver docs secao 4.1.
    [Serializable]
    public class DynamicTurnResult
    {
        public string fala;
        public string[] opcoes;

        // Um papel por opcao (ver OptionRoles).
        public string[] papeis_opcoes;

        public bool encerrar;

        // Nome exato da carta entregue neste turno, ou "" (nao revela).
        // Quando preenchido, o jogo abre o painel "Pista Revelada" com essa
        // carta - o mesmo efeito do comando <<reveal>> dos roteirizados.
        public string revelar_pista;

        // Carta a que a opcao "pedido_carta" deste turno se refere, ou "".
        public string carta_alvo;
    }

    // Papeis possiveis de uma opcao. Espelham o esqueleto de conversa dos
    // NPCs roteirizados (diario, secao 08): temas de abertura, aprofundar,
    // pedir a carta indicada, pressionar, insistir, acusar e sair.
    public static class OptionRoles
    {
        public const string Tema = "tema";
        public const string Aprofundar = "aprofundar";
        public const string PedidoCarta = "pedido_carta";
        public const string Pressionar = "pressionar";
        public const string Insistir = "insistir";
        public const string Acusar = "acusar";
        public const string Saida = "saida";

        public static readonly string[] Todos =
            { Tema, Aprofundar, PedidoCarta, Pressionar, Insistir, Acusar, Saida };
    }
}

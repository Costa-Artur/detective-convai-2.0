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

    [Serializable]
    internal class TurnSchemaProperties
    {
        public SchemaField fala = new SchemaField { type = "string" };
        public SchemaArrayField opcoes;
        public SchemaField encerrar = new SchemaField { type = "boolean" };

        // Equivalente ao comando <<reveal>> dos .yarn: e assim que o NPC
        // dinamico ENTREGA uma carta ao jogador, e nao apenas fala sobre ela.
        // String vazia = nao revela nada neste turno. Nao e opcional no schema
        // porque o modo estrito exige que toda propriedade seja obrigatoria -
        // dai o uso de "" em vez de null.
        public SchemaField revelar_pista = new SchemaField { type = "string" };
    }

    [Serializable]
    internal class TurnSchema
    {
        public string type = "object";
        public TurnSchemaProperties properties;
        public string[] required = { "fala", "opcoes", "encerrar", "revelar_pista" };
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
        public bool encerrar;

        // "suspeito" | "arma do crime" | "local" | "" (vazio = nao revela).
        // Quando preenchido, o jogo abre o painel "Pista Revelada" com uma
        // carta daquele tipo do inventario do NPC - o mesmo efeito que o
        // comando <<reveal>> produz nos NPCs roteirizados.
        public string revelar_pista;
    }
}

using System.Text;

namespace Conde.Json;

/// <summary>
/// Escreve JSON — compacto ou com recuo.
/// </summary>
/// <remarks>
/// <para>
/// Escrever é mais fácil que ler, e tem um lugar onde dá para errar feio: o que
/// escapar dentro de um texto.
/// </para>
/// <para>
/// A regra mínima é curta — aspas, barra invertida e tudo abaixo de 0x20 —, e
/// quase toda biblioteca escapa mais do que isso. O <c>System.Text.Json</c>,
/// por padrão, escapa também <c>&lt;</c>, <c>&gt;</c>, <c>&amp;</c>, <c>'</c> e
/// o <c>+</c>, e não é exagero: é para que o JSON possa ser colado dentro de um
/// <c>&lt;script&gt;</c> numa página sem fechar a etiqueta. O clássico
/// <c>&lt;/script&gt;</c> dentro de um texto já derrubou muita página e abriu
/// muito XSS.
/// </para>
/// <para>
/// Aqui a escolha é a mínima, com a fuga de HTML disponível para quem precisar.
/// O que não dá é escolher sem saber que a escolha existe.
/// </para>
/// </remarks>
public static class Escritor
{
    public sealed record Estilo(
        bool ComRecuo = false,
        int Recuo = 2,
        bool FugirHtml = false)
    {
        public static readonly Estilo Compacto = new();

        public static readonly Estilo Bonito = new(ComRecuo: true);

        /// <summary>Seguro para colar dentro de um <c>&lt;script&gt;</c>.</summary>
        public static readonly Estilo ParaPagina = new(FugirHtml: true);
    }

    public static string Escrever(Valor valor) => Escrever(valor, Estilo.Compacto);

    public static string Escrever(Valor valor, Estilo estilo)
    {
        var texto = new StringBuilder();

        Escrever(texto, valor, estilo, 0);

        return texto.ToString();
    }

    private static void Escrever(StringBuilder saida, Valor valor, Estilo estilo, int nivel)
    {
        switch (valor)
        {
            case Valor.Nulo:
                saida.Append("null");
                break;

            case Valor.Booleano booleano:
                saida.Append(booleano.Quanto ? "true" : "false");
                break;

            case Valor.Numero numero:
                // O texto original, e não o `double` reformatado. É o que faz
                // um `1e400` continuar `1e400` e um número de trinta dígitos
                // não virar notação científica.
                saida.Append(numero.Bruto);
                break;

            case Valor.Texto texto:
                EscreverTexto(saida, texto.Quanto, estilo);
                break;

            case Valor.Vetor vetor:
                EscreverVetor(saida, vetor, estilo, nivel);
                break;

            case Valor.Objeto objeto:
                EscreverObjeto(saida, objeto, estilo, nivel);
                break;

            default:
                throw new InvalidOperationException($"valor desconhecido: {valor.GetType().Name}");
        }
    }

    private static void EscreverVetor(StringBuilder saida, Valor.Vetor vetor,
        Estilo estilo, int nivel)
    {
        if (vetor.Itens.Count == 0)
        {
            saida.Append("[]");

            return;
        }

        saida.Append('[');

        for (var i = 0; i < vetor.Itens.Count; i++)
        {
            if (i > 0)
            {
                saida.Append(',');
            }

            Quebrar(saida, estilo, nivel + 1);
            Escrever(saida, vetor.Itens[i], estilo, nivel + 1);
        }

        Quebrar(saida, estilo, nivel);
        saida.Append(']');
    }

    private static void EscreverObjeto(StringBuilder saida, Valor.Objeto objeto,
        Estilo estilo, int nivel)
    {
        if (objeto.Pares.Count == 0)
        {
            saida.Append("{}");

            return;
        }

        saida.Append('{');

        for (var i = 0; i < objeto.Pares.Count; i++)
        {
            if (i > 0)
            {
                saida.Append(',');
            }

            Quebrar(saida, estilo, nivel + 1);

            EscreverTexto(saida, objeto.Pares[i].Nome, estilo);

            saida.Append(':');

            if (estilo.ComRecuo)
            {
                saida.Append(' ');
            }

            Escrever(saida, objeto.Pares[i].Valor, estilo, nivel + 1);
        }

        Quebrar(saida, estilo, nivel);
        saida.Append('}');
    }

    private static void Quebrar(StringBuilder saida, Estilo estilo, int nivel)
    {
        if (!estilo.ComRecuo)
        {
            return;
        }

        saida.Append('\n').Append(new string(' ', estilo.Recuo * nivel));
    }

    private static void EscreverTexto(StringBuilder saida, string texto, Estilo estilo)
    {
        saida.Append('"');

        foreach (var letra in texto)
        {
            switch (letra)
            {
                case '"': saida.Append("\\\""); break;
                case '\\': saida.Append("\\\\"); break;
                case '\b': saida.Append("\\b"); break;
                case '\f': saida.Append("\\f"); break;
                case '\n': saida.Append("\\n"); break;
                case '\r': saida.Append("\\r"); break;
                case '\t': saida.Append("\\t"); break;

                case '<' or '>' or '&' or '\'' or '+' when estilo.FugirHtml:
                    saida.Append($"\\u{(int)letra:x4}");
                    break;

                default:
                    if (letra < 0x20)
                    {
                        saida.Append($"\\u{(int)letra:x4}");
                    }
                    else
                    {
                        saida.Append(letra);
                    }

                    break;
            }
        }

        saida.Append('"');
    }
}

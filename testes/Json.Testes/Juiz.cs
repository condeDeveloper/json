using System.Text;
using Stj = System.Text.Json;

namespace Conde.Json.Testes;

/// <summary>
/// O juiz: o <c>System.Text.Json</c> do próprio .NET.
/// </summary>
/// <remarks>
/// <para>
/// A comparação é nos <b>dois sentidos</b>, e os dois valem por razões
/// diferentes:
/// </para>
/// <list type="bullet">
///   <item><description>
///     <b>O que ele aceita, eu aceito.</b> Recusar JSON válido é o defeito que
///     quebra a integração de alguém.
///   </description></item>
///   <item><description>
///     <b>O que ele recusa, eu recuso.</b> Aceitar o que não é JSON é pior e
///     passa muito mais despercebido: o analisador funciona com todo arquivo
///     que o autor testou, e um dia lê <c>{a:1}</c> sem reclamar.
///   </description></item>
/// </list>
/// <para>
/// A segunda metade é a que quase ninguém testa, e é a que este juiz cobre de
/// graça: basta estragar um documento válido de um jeito qualquer e exigir que
/// os dois concordem sobre o estrago.
/// </para>
/// </remarks>
public static class Juiz
{
    /// <summary>O <c>System.Text.Json</c> aceita este documento?</summary>
    public static bool Aceita(string texto)
    {
        try
        {
            // `Deserialize` com `JsonDocument` percorre o documento inteiro e
            // é o mais perto que dá de "só valide isto".
            using var documento = Stj.JsonDocument.Parse(
                texto,
                new Stj.JsonDocumentOptions
                {
                    CommentHandling = Stj.JsonCommentHandling.Disallow,
                    AllowTrailingCommas = false,
                    MaxDepth = Analisador.ProfundidadeMaxima,
                });

            return true;
        }
        catch (Stj.JsonException)
        {
            return false;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    public static bool EuAceito(string texto) => Analisador.Vale(texto, out _, out _);

    public sealed record Divergencia(string Documento, bool EuAceitei, bool EleAceitou)
    {
        public override string ToString()
        {
            var meu = EuAceitei ? "aceitei" : "recusei";
            var dele = EleAceitou ? "aceitou" : "recusou";

            return $"{Mostrar(Documento)}: eu {meu}, o System.Text.Json {dele}";
        }
    }

    public static Divergencia? Comparar(string texto)
    {
        var eu = EuAceito(texto);
        var ele = Aceita(texto);

        return eu == ele ? null : new Divergencia(texto, eu, ele);
    }

    public static string Mostrar(string texto)
    {
        var curto = texto.Length > 60 ? texto[..59] + "…" : texto;

        return '"' + curto
            .Replace("\n", "\\n").Replace("\t", "\\t").Replace("\r", "\\r") + '"';
    }

    // -- os geradores ------------------------------------------------------

    /// <summary>
    /// Um documento JSON válido, sorteado pela gramática.
    /// </summary>
    /// <remarks>
    /// Montar pela gramática dá documentos sempre válidos e sempre estranhos.
    /// O alfabeto dos textos inclui de propósito o que costuma quebrar
    /// escritores: aspas, barra invertida, quebra de linha, caracteres de
    /// controle, acentos e um caractere fora do plano básico.
    /// </remarks>
    public static string SortearValido(Random sorteio, int profundidade = 0)
    {
        var formas = profundidade > 3 ? 4 : 6;

        return sorteio.Next(formas) switch
        {
            0 => sorteio.Next(3) switch
            {
                0 => "null",
                1 => "true",
                _ => "false",
            },
            1 => SortearNumero(sorteio),
            2 or 3 => SortearTexto(sorteio),
            4 => SortearVetor(sorteio, profundidade),
            _ => SortearObjeto(sorteio, profundidade),
        };
    }

    private static string SortearNumero(Random sorteio) => sorteio.Next(10) switch
    {
        0 => "0",
        1 => "-0",
        2 => sorteio.Next(-1000, 1000).ToString(),
        3 => "1e" + sorteio.Next(-30, 30),
        4 => "1E+" + sorteio.Next(0, 30),
        5 => "-1.5e-" + sorteio.Next(1, 30),
        6 => "123456789012345678901234567890",
        7 => "0.1",
        8 => "1e400",
        _ => $"{sorteio.Next(0, 1000)}.{sorteio.Next(0, 1000)}",
    };

    private static readonly char[] Alfabeto =
        ['a', 'b', 'z', '0', '9', ' ', '\t', '\n', '"', '\\', '/', '\b', '\f',
         '\r', 'á', 'ç', '日', '\u0001', '\u001f', '\u007f'];

    private static string SortearTexto(Random sorteio)
    {
        var quantos = sorteio.Next(6);
        var cru = new StringBuilder();

        for (var i = 0; i < quantos; i++)
        {
            cru.Append(Alfabeto[sorteio.Next(Alfabeto.Length)]);
        }

        if (sorteio.Next(6) == 0)
        {
            // Um caractere fora do plano básico, que em JSON é um par de \u.
            cru.Append("\U0001F600");
        }

        return Escritor.Escrever(new Valor.Texto(cru.ToString()));
    }

    private static string SortearVetor(Random sorteio, int profundidade)
    {
        var quantos = sorteio.Next(4);
        var itens = new List<string>();

        for (var i = 0; i < quantos; i++)
        {
            itens.Add(SortearValido(sorteio, profundidade + 1));
        }

        return "[" + string.Join(",", itens) + "]";
    }

    private static string SortearObjeto(Random sorteio, int profundidade)
    {
        var quantos = sorteio.Next(4);
        var pares = new List<string>();

        for (var i = 0; i < quantos; i++)
        {
            var nome = SortearTexto(sorteio);

            pares.Add($"{nome}:{SortearValido(sorteio, profundidade + 1)}");
        }

        return "{" + string.Join(",", pares) + "}";
    }

    /// <summary>
    /// Estraga um documento de um jeito qualquer.
    /// </summary>
    /// <remarks>
    /// É o gerador que vale mais. Documentos sorteados testam o que o
    /// analisador aceita; documentos <b>estragados</b> testam o que ele recusa,
    /// que é a metade difícil — e a maioria dos estragos produz algo que
    /// continua parecendo JSON de longe.
    /// </remarks>
    public static string Estragar(string valido, Random sorteio)
    {
        if (valido.Length == 0)
        {
            return "{";
        }

        var onde = sorteio.Next(valido.Length);

        return sorteio.Next(8) switch
        {
            // troca um caractere por outro do próprio documento
            0 => valido[..onde] + valido[sorteio.Next(valido.Length)] + valido[(onde + 1)..],

            // apaga um caractere
            1 => valido[..onde] + valido[(onde + 1)..],

            // repete um caractere
            2 => valido[..onde] + valido[onde] + valido[onde..],

            // corta o documento no meio
            3 => valido[..onde],

            // acrescenta um caractere de pontuação
            4 => valido[..onde] + ",:{}[]\"\\"[sorteio.Next(8)] + valido[onde..],

            // cola lixo no fim -- o clássico "e ainda há bytes depois"
            5 => valido + "abc"[sorteio.Next(3)],

            // deixa um espaço estranho no meio: nem tudo o que parece branco é
            6 => valido[..onde] + "\v\f\u00a0\u2028"[sorteio.Next(4)] + valido[onde..],

            // vírgula sobrando antes do fecho
            _ => valido.Replace("]", ",]").Replace("}", ",}"),
        };
    }

    public sealed record Placar(int Comparados, int Concordaram, List<Divergencia> Sobras)
    {
        public double Taxa => Comparados == 0 ? 0 : 100.0 * Concordaram / Comparados;
    }

    /// <summary>Roda um lote e devolve o placar.</summary>
    public static Placar Lote(int semente, int quantos, bool estragando)
    {
        var sorteio = new Random(semente);

        var comparados = 0;
        var concordaram = 0;
        var sobras = new List<Divergencia>();

        for (var i = 0; i < quantos; i++)
        {
            var documento = SortearValido(sorteio);

            if (estragando)
            {
                documento = Estragar(documento, sorteio);
            }

            comparados++;

            var divergencia = Comparar(documento);

            if (divergencia is null)
            {
                concordaram++;
            }
            else if (sobras.Count < 10)
            {
                sobras.Add(divergencia);
            }
        }

        return new Placar(comparados, concordaram, sobras);
    }
}

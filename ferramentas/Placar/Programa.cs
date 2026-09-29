using System.Text;
using Conde.Json;
using Stj = System.Text.Json;

namespace Conde.Json.Ferramentas.Placar;

/// <summary>
/// O placar: quanto este analisador concorda com o <c>System.Text.Json</c>.
/// </summary>
/// <remarks>
/// <para>
/// A comparação é nos dois sentidos, e a segunda coluna é a que vale: aceitar
/// JSON válido é fácil e recusar o que não é JSON é o trabalho todo.
/// </para>
/// </remarks>
public static class Programa
{
    public static int Main(string[] argumentos)
    {
        // Le o primeiro argumento que for um numero, e ignora o resto. O
        // `dotnet run` nem sempre entrega so o que vem depois do `--`.
        var quantos = 20_000;

        foreach (var argumento in argumentos)
        {
            if (int.TryParse(argumento, out var lido) && lido > 0)
            {
                quantos = lido;
                break;
            }
        }

        Console.WriteLine($"{quantos} documentos por corpus, contra o "
            + "System.Text.Json rodando de verdade.");
        Console.WriteLine();

        Console.WriteLine("corpus".PadRight(26)
            + "comparados".PadLeft(12) + "concordância".PadLeft(14));
        Console.WriteLine(new string('-', 52));

        var todos = 0;
        var certos = 0;

        var sobras = new List<string>();

        foreach (var (nome, estragando) in ((string, bool)[])
                 [("válidos", false), ("estragados", true)])
        {
            var comparados = 0;
            var concordaram = 0;

            for (var semente = 1; semente <= 3; semente++)
            {
                var sorteio = new Random(semente);

                for (var i = 0; i < quantos; i++)
                {
                    var documento = SortearValido(sorteio);

                    if (estragando)
                    {
                        documento = Estragar(documento, sorteio);
                    }

                    comparados++;

                    var eu = Analisador.Vale(documento, out _, out _);
                    var ele = Aceita(documento);

                    if (eu == ele)
                    {
                        concordaram++;
                    }
                    else if (sobras.Count < 6)
                    {
                        sobras.Add(
                            $"{Mostrar(documento)}: eu {(eu ? "aceitei" : "recusei")}, "
                            + $"ele {(ele ? "aceitou" : "recusou")}");
                    }
                }
            }

            todos += comparados;
            certos += concordaram;

            Console.WriteLine(nome.PadRight(26)
                + comparados.ToString().PadLeft(12)
                + $"{100.0 * concordaram / comparados:F3}%".PadLeft(14));
        }

        Console.WriteLine(new string('-', 52));
        Console.WriteLine("total".PadRight(26)
            + todos.ToString().PadLeft(12)
            + $"{100.0 * certos / todos:F3}%".PadLeft(14));

        Console.WriteLine();
        Console.WriteLine("o que os dois recusam, e que parece JSON:");
        Console.WriteLine();

        foreach (var caso in (string[])
                 ["01", "+1", ".5", "1.", "0x1F", "NaN", "Infinity",
                  "'aspas'", "{a:1}", "[1,]", "{\"a\":1,}", "// nada", "1 2"])
        {
            var eu = Analisador.Vale(caso, out _, out _);
            var ele = Aceita(caso);

            var marca = eu == ele ? "  " : "!!";

            Console.WriteLine($"  {marca} {caso.PadRight(14)} "
                + $"eu: {(eu ? "aceito" : "recuso")}   "
                + $"System.Text.Json: {(ele ? "aceita" : "recusa")}");
        }

        if (sobras.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("as divergências:");
            Console.WriteLine();

            foreach (var sobra in sobras)
            {
                Console.WriteLine("  " + sobra);
            }
        }

        return certos == todos ? 0 : 1;
    }

    private static bool Aceita(string texto)
    {
        try
        {
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

    private static string Mostrar(string texto)
    {
        var curto = texto.Length > 50 ? texto[..49] + "…" : texto;

        return '"' + curto.Replace("\n", "\\n").Replace("\t", "\\t") + '"';
    }

    // -- os geradores, iguais aos dos testes -------------------------------

    private static string SortearValido(Random sorteio, int profundidade = 0)
    {
        var formas = profundidade > 3 ? 4 : 6;

        return sorteio.Next(formas) switch
        {
            0 => sorteio.Next(3) switch { 0 => "null", 1 => "true", _ => "false" },
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
            cru.Append("\U0001F600");
        }

        return Escritor.Escrever(new Valor.Texto(cru.ToString()));
    }

    private static string SortearVetor(Random sorteio, int profundidade)
    {
        var itens = new List<string>();

        for (var i = 0; i < sorteio.Next(4); i++)
        {
            itens.Add(SortearValido(sorteio, profundidade + 1));
        }

        return "[" + string.Join(",", itens) + "]";
    }

    private static string SortearObjeto(Random sorteio, int profundidade)
    {
        var pares = new List<string>();

        for (var i = 0; i < sorteio.Next(4); i++)
        {
            pares.Add($"{SortearTexto(sorteio)}:{SortearValido(sorteio, profundidade + 1)}");
        }

        return "{" + string.Join(",", pares) + "}";
    }

    private static string Estragar(string valido, Random sorteio)
    {
        if (valido.Length == 0)
        {
            return "{";
        }

        var onde = sorteio.Next(valido.Length);

        return sorteio.Next(8) switch
        {
            0 => valido[..onde] + valido[sorteio.Next(valido.Length)] + valido[(onde + 1)..],
            1 => valido[..onde] + valido[(onde + 1)..],
            2 => valido[..onde] + valido[onde] + valido[onde..],
            3 => valido[..onde],
            4 => valido[..onde] + ",:{}[]\"\\"[sorteio.Next(8)] + valido[onde..],
            5 => valido + "abc"[sorteio.Next(3)],
            6 => valido[..onde] + "\v\f\u00a0\u2028"[sorteio.Next(4)] + valido[onde..],
            _ => valido.Replace("]", ",]").Replace("}", ",}"),
        };
    }
}

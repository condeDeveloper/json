using System.Text;
using Conde.Json;

namespace Conde.Json.Ferramentas.Cli;

/// <summary>
/// A linha de comando: formatar, validar, comprimir e olhar por dentro.
/// </summary>
/// <remarks>
/// <para>
/// O comando <c>conferir</c> é o que justifica a ferramenta. Quando um arquivo
/// de configuração de duzentas linhas não carrega, a mensagem que a maioria das
/// bibliotecas dá é "JSON inválido" — e aí a pessoa lê duzentas linhas
/// procurando uma vírgula. Aqui sai a linha, a coluna e a linha do arquivo com
/// um acento circunflexo embaixo do caractere.
/// </para>
/// </remarks>
public static class Programa
{
    public static int Main(string[] argumentos)
    {
        if (argumentos.Length < 1)
        {
            Console.WriteLine(Ajuda);

            return 1;
        }

        var comando = argumentos[0];

        string documento;

        try
        {
            documento = argumentos.Length > 1
                ? File.ReadAllText(argumentos[1], Encoding.UTF8)
                : Console.In.ReadToEnd();
        }
        catch (IOException erro)
        {
            Console.Error.WriteLine(erro.Message);

            return 2;
        }

        return comando switch
        {
            "formatar" => Formatar(documento, Escritor.Estilo.Bonito),
            "comprimir" => Formatar(documento, Escritor.Estilo.Compacto),
            "paraPagina" => Formatar(documento, Escritor.Estilo.ParaPagina),
            "conferir" => Conferir(documento),
            "olhar" => Olhar(documento),
            _ => Desconhecido(comando),
        };
    }

    private static int Desconhecido(string comando)
    {
        Console.Error.WriteLine($"não conheço o comando {comando}");
        Console.WriteLine(Ajuda);

        return 1;
    }

    private static int Formatar(string documento, Escritor.Estilo estilo)
    {
        if (!Analisador.Vale(documento, out var valor, out var erro))
        {
            Console.Error.WriteLine(erro!.Apontar(documento));

            return 2;
        }

        Console.WriteLine(Escritor.Escrever(valor!, estilo));

        return 0;
    }

    private static int Conferir(string documento)
    {
        if (!Analisador.Vale(documento, out var valor, out var erro))
        {
            Console.Error.WriteLine(erro!.Apontar(documento));

            return 2;
        }

        var compacto = Escritor.Escrever(valor!);

        Console.WriteLine($"JSON válido: {documento.Length} byte(s), "
            + $"{compacto.Length} comprimido.");

        var repetidos = ContarNomesRepetidos(valor!);

        if (repetidos > 0)
        {
            // Não é erro pela especificação, e é quase sempre um engano. A RFC
            // 8259 chama o caso de "indefinido", e já foi vulnerabilidade de
            // verdade: um sistema valida a primeira chave e outro usa a última.
            Console.WriteLine($"AVISO: {repetidos} objeto(s) com nome de campo repetido.");
        }

        Console.WriteLine($"profundidade: {Profundidade(valor!)}");

        return 0;
    }

    private static int Olhar(string documento)
    {
        if (!Analisador.Vale(documento, out var valor, out var erro))
        {
            Console.Error.WriteLine(erro!.Apontar(documento));

            return 2;
        }

        Mostrar(valor!, 0, null);

        return 0;
    }

    private static void Mostrar(Valor valor, int nivel, string? nome)
    {
        var recuo = new string(' ', nivel * 2);
        var rotulo = nome is null ? string.Empty : $"{nome}: ";

        switch (valor)
        {
            case Valor.Objeto objeto:
                Console.WriteLine($"{recuo}{rotulo}objeto com {objeto.Pares.Count} campo(s)");

                foreach (var (chave, dentro) in objeto.Pares)
                {
                    Mostrar(dentro, nivel + 1, chave);
                }

                break;

            case Valor.Vetor vetor:
                Console.WriteLine($"{recuo}{rotulo}vetor com {vetor.Itens.Count} item(ns)");

                for (var i = 0; i < vetor.Itens.Count; i++)
                {
                    Mostrar(vetor.Itens[i], nivel + 1, $"[{i}]");
                }

                break;

            case Valor.Numero numero:
                Console.WriteLine($"{recuo}{rotulo}número {numero.Bruto}"
                    + (numero.EhInteiro ? " (inteiro)" : string.Empty));
                break;

            case Valor.Texto texto:
                var curto = texto.Quanto.Length > 40
                    ? texto.Quanto[..39] + "…"
                    : texto.Quanto;

                Console.WriteLine($"{recuo}{rotulo}texto \"{curto}\" "
                    + $"({texto.Quanto.Length} caractere(s))");
                break;

            default:
                Console.WriteLine($"{recuo}{rotulo}{valor}");
                break;
        }
    }

    private static int ContarNomesRepetidos(Valor valor) => valor switch
    {
        Valor.Objeto objeto =>
            (objeto.TemNomeRepetido() ? 1 : 0)
            + objeto.Pares.Sum(par => ContarNomesRepetidos(par.Valor)),
        Valor.Vetor vetor => vetor.Itens.Sum(ContarNomesRepetidos),
        _ => 0,
    };

    private static int Profundidade(Valor valor) => valor switch
    {
        Valor.Objeto objeto => objeto.Pares.Count == 0
            ? 1
            : 1 + objeto.Pares.Max(par => Profundidade(par.Valor)),
        Valor.Vetor vetor => vetor.Itens.Count == 0
            ? 1
            : 1 + vetor.Itens.Max(Profundidade),
        _ => 1,
    };

    private const string Ajuda = """
        json -- lê e escreve JSON, do zero

          json formatar   [ARQUIVO]   com recuo
          json comprimir  [ARQUIVO]   sem um espaço sobrando
          json paraPagina [ARQUIVO]   seguro para colar dentro de <script>
          json conferir   [ARQUIVO]   é JSON? e o que há de estranho nele
          json olhar      [ARQUIVO]   a árvore, campo por campo

        Sem ARQUIVO, lê a entrada padrão.
        O código de saída é 2 quando o documento não é JSON.
        """;
}

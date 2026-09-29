using Xunit;
using Stj = System.Text.Json;

namespace Conde.Json.Testes;

/// <summary>
/// A conformidade com o <c>System.Text.Json</c>, nos dois sentidos.
/// </summary>
public class JuizTest
{
    [Fact(DisplayName = "cinquenta mil documentos válidos: os dois aceitam")]
    public void OQueEleAceitaEuAceito()
    {
        foreach (var semente in (int[])[1, 2, 3, 4, 20260929])
        {
            var placar = Juiz.Lote(semente, 10_000, estragando: false);

            Assert.True(placar.Concordaram == placar.Comparados,
                $"semente {semente}: {placar.Comparados - placar.Concordaram} de "
                + $"{placar.Comparados} divergiram. A primeira: "
                + $"{placar.Sobras.FirstOrDefault()}");
        }
    }

    [Fact(DisplayName = "cinquenta mil documentos estragados: os dois concordam")]
    public void OQueEleRecusaEuRecuso()
    {
        // Esta é a metade que quase ninguém testa, e é a difícil. A maioria dos
        // estragos produz algo que continua parecendo JSON de longe -- uma
        // vírgula a mais, um caractere trocado, um espaço que não é espaço --
        // e cada um deles é uma chance de o analisador ser permissivo demais.
        foreach (var semente in (int[])[1, 2, 3, 4, 20260929])
        {
            var placar = Juiz.Lote(semente, 10_000, estragando: true);

            Assert.True(placar.Concordaram == placar.Comparados,
                $"semente {semente}: {placar.Comparados - placar.Concordaram} de "
                + $"{placar.Comparados} divergiram. A primeira: "
                + $"{placar.Sobras.FirstOrDefault()}");
        }
    }

    [Theory(DisplayName = "o que parece JSON e não é")]
    // números
    [InlineData("01")]
    [InlineData("-01")]
    [InlineData("+1")]
    [InlineData(".5")]
    [InlineData("1.")]
    [InlineData("1.e5")]
    [InlineData("1e")]
    [InlineData("1e+")]
    [InlineData("0x1F")]
    [InlineData("Infinity")]
    [InlineData("-Infinity")]
    [InlineData("NaN")]
    [InlineData("1_000")]
    [InlineData("- 1")]
    // textos
    [InlineData("'aspas simples'")]
    [InlineData("\"sem fechar")]
    [InlineData("\"\\x41\"")]
    [InlineData("\"\\u12\"")]
    [InlineData("\"\\uZZZZ\"")]
    // objetos e vetores
    [InlineData("{a: 1}")]
    [InlineData("{\"a\" 1}")]
    [InlineData("{\"a\": 1,}")]
    [InlineData("[1, 2,]")]
    [InlineData("[1 2]")]
    [InlineData("{")]
    [InlineData("[")]
    [InlineData("}")]
    [InlineData("]")]
    [InlineData("[}")]
    [InlineData("{]")]
    [InlineData("{\"a\": }")]
    // o resto
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("undefined")]
    [InlineData("True")]
    [InlineData("NULL")]
    [InlineData("1 2")]
    [InlineData("{} {}")]
    [InlineData("// comentario\n1")]
    [InlineData("/* comentario */ 1")]
    [InlineData("[1]extra")]
    public void ONaoEhJson(string documento)
    {
        Assert.False(Juiz.EuAceito(documento),
            $"{Juiz.Mostrar(documento)} não é JSON e eu aceitei");

        // E o juiz confirma. Se um dia o System.Text.Json passar a aceitar
        // algum destes, este teste avisa antes de a divergência virar defeito.
        Assert.False(Juiz.Aceita(documento),
            $"{Juiz.Mostrar(documento)}: o System.Text.Json aceita e eu não");
    }

    [Theory(DisplayName = "o que é JSON e parece que não")]
    [InlineData("0")]
    [InlineData("-0")]
    [InlineData("1e400")]
    [InlineData("-1e-400")]
    [InlineData("123456789012345678901234567890")]
    [InlineData("\"\"")]
    [InlineData("\"\\u0000\"")]
    [InlineData("\"\\/\"")]
    [InlineData("\"\\ud83d\\ude00\"")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("[[[[[]]]]]")]
    [InlineData("{\"\":\"\"}")]
    [InlineData("{\"a\":1,\"a\":2}")]
    [InlineData("  \t\r\n  1  \t\r\n  ")]
    [InlineData("\"\\u00e7\"")]
    public void EhJsonSim(string documento)
    {
        Assert.True(Juiz.EuAceito(documento),
            $"{Juiz.Mostrar(documento)} é JSON e eu recusei");

        Assert.True(Juiz.Aceita(documento),
            $"{Juiz.Mostrar(documento)}: eu aceito e o System.Text.Json não");
    }

    [Fact(DisplayName = "os valores lidos são os mesmos que o System.Text.Json lê")]
    public void OsValoresBatem()
    {
        // Concordar em aceitar não basta: o que saiu tem de ser o mesmo. Um
        // analisador pode aceitar `{"a":1}` e guardar o valor errado.
        var sorteio = new Random(20260929);

        for (var i = 0; i < 20_000; i++)
        {
            var documento = Juiz.SortearValido(sorteio);

            var meu = Analisador.Ler(documento);

            using var dele = Stj.JsonDocument.Parse(documento);

            Conferir(meu, dele.RootElement, documento);
        }
    }

    private static void Conferir(Valor meu, Stj.JsonElement dele, string documento)
    {
        switch (dele.ValueKind)
        {
            case Stj.JsonValueKind.Null:
                Assert.True(meu is Valor.Nulo, $"{Juiz.Mostrar(documento)}: esperava nulo");
                break;

            case Stj.JsonValueKind.True:
            case Stj.JsonValueKind.False:
                Assert.True(meu is Valor.Booleano booleano
                    && booleano.Quanto == dele.GetBoolean(),
                    $"{Juiz.Mostrar(documento)}: booleano diferente");
                break;

            case Stj.JsonValueKind.Number:
                var numero = Assert.IsType<Valor.Numero>(meu);

                // O texto bruto tem de ser o mesmo -- e não só o `double`. É o
                // que garante que um `1e400` ou um número de trinta dígitos
                // atravessa sem perder nada.
                Assert.Equal(dele.GetRawText(), numero.Bruto);
                break;

            case Stj.JsonValueKind.String:
                var texto = Assert.IsType<Valor.Texto>(meu);

                Assert.Equal(dele.GetString(), texto.Quanto);
                break;

            case Stj.JsonValueKind.Array:
                var vetor = Assert.IsType<Valor.Vetor>(meu);
                var itens = dele.EnumerateArray().ToList();

                Assert.Equal(itens.Count, vetor.Itens.Count);

                for (var i = 0; i < itens.Count; i++)
                {
                    Conferir(vetor.Itens[i], itens[i], documento);
                }

                break;

            case Stj.JsonValueKind.Object:
                var objeto = Assert.IsType<Valor.Objeto>(meu);
                var pares = dele.EnumerateObject().ToList();

                Assert.Equal(pares.Count, objeto.Pares.Count);

                for (var i = 0; i < pares.Count; i++)
                {
                    // A ordem também: o System.Text.Json preserva a do
                    // documento, e um analisador que use dicionário comum
                    // embaralha e ninguém percebe até reescrever o arquivo.
                    Assert.Equal(pares[i].Name, objeto.Pares[i].Nome);

                    Conferir(objeto.Pares[i].Valor, pares[i].Value, documento);
                }

                break;

            default:
                Assert.Fail($"tipo inesperado: {dele.ValueKind}");
                break;
        }
    }

    [Fact(DisplayName = "escrever e ler de volta dá o mesmo valor")]
    public void IdaEVolta()
    {
        var sorteio = new Random(7);

        for (var i = 0; i < 20_000; i++)
        {
            var documento = Juiz.SortearValido(sorteio);
            var valor = Analisador.Ler(documento);

            foreach (var estilo in (Escritor.Estilo[])
                     [Escritor.Estilo.Compacto, Escritor.Estilo.Bonito,
                      Escritor.Estilo.ParaPagina])
            {
                var escrito = Escritor.Escrever(valor, estilo);

                Assert.True(Juiz.Aceita(escrito),
                    $"escrevi algo que o System.Text.Json não lê: {Juiz.Mostrar(escrito)}");

                var devolta = Analisador.Ler(escrito);

                Assert.Equal(
                    Escritor.Escrever(valor),
                    Escritor.Escrever(devolta));
            }
        }
    }

    [Fact(DisplayName = "o aninhamento sem fim é recusado em vez de derrubar a pilha")]
    public void AninhamentoSemFim()
    {
        // Cem mil colchetes são cem mil bytes, e derrubam qualquer analisador
        // recursivo que não tenha teto. Não é caso hipotético: é uma entrada de
        // duas linhas de código que derruba um serviço.
        var fundo = new string('[', 100_000) + new string(']', 100_000);

        var erro = Assert.Throws<ErroDeJson>(() => Analisador.Ler(fundo));

        Assert.Contains("aninhamento", erro.Message);

        // E o que cabe no teto continua passando.
        var raso = new string('[', 60) + new string(']', 60);

        Assert.True(Juiz.EuAceito(raso));
        Assert.True(Juiz.Aceita(raso));
    }

    [Fact(DisplayName = "o erro diz a linha e a coluna, e aponta")]
    public void OErroAponta()
    {
        const string documento = "{\n  \"nome\": \"ok\",\n  \"idade\": 01\n}";

        var erro = Assert.Throws<ErroDeJson>(() => Analisador.Ler(documento));

        Assert.Equal(3, erro.Linha);
        Assert.Contains("zero à esquerda", erro.Message);

        var apontado = erro.Apontar(documento);

        Assert.Contains("^", apontado);
        Assert.Contains("idade", apontado);
    }
}

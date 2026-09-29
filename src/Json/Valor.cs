using System.Globalization;
using System.Text;

namespace Conde.Json;

/// <summary>
/// Um valor de JSON. São seis, e é isso.
/// </summary>
/// <remarks>
/// <para>
/// A lista inteira do formato cabe numa linha: objeto, vetor, texto, número,
/// booleano, nulo. Não há data, não há inteiro separado de decimal, não há
/// bytes, não há comentário. Douglas Crockford descreveu o formato num
/// cartão de visita em 2001, e a parte mais difícil de escrever um analisador
/// não é o que há — é o que <b>não</b> há e as pessoas insistem em usar.
/// </para>
/// <para>
/// O número é o caso interessante. O JSON não diz quantos bits ele tem, nem se
/// é binário ou decimal: diz só a <i>sintaxe</i>. Um <c>1e400</c> é JSON válido
/// e não cabe num <c>double</c>; um <c>123456789012345678901234567890</c> é
/// JSON válido e não cabe num <c>long</c>. Guardar o texto original ao lado do
/// <c>double</c> é o que permite não perder nada — e é o que quase toda
/// biblioteca deixa de fazer.
/// </para>
/// </remarks>
public abstract record Valor
{
    public sealed record Nulo : Valor
    {
        public static readonly Nulo Unico = new();

        public override string ToString() => "null";
    }

    public sealed record Booleano(bool Quanto) : Valor
    {
        public override string ToString() => Quanto ? "true" : "false";
    }

    /// <summary>
    /// Um número, com o texto original do lado.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Guardar o texto não é zelo: é a única forma de não mentir. Um
    /// <c>1e400</c> vira <c>+∞</c> como <c>double</c>, e um
    /// <c>0.1000000000000000055511151231257827</c> vira <c>0.1</c> — o primeiro
    /// perde a validade e o segundo perde os dígitos. Quem só quer contas usa
    /// o <c>double</c>; quem vai reescrever o documento usa o texto, e o
    /// documento sai igual ao que entrou.
    /// </para>
    /// </remarks>
    public sealed record Numero(string Bruto) : Valor
    {
        /// <summary>O valor como <c>double</c>, com a perda que isso implica.</summary>
        public double Quanto => double.Parse(Bruto, CultureInfo.InvariantCulture);

        /// <summary>O número é inteiro, pela sintaxe? Sem ponto nem expoente.</summary>
        public bool EhInteiro =>
            !Bruto.Contains('.') && !Bruto.Contains('e') && !Bruto.Contains('E');

        public override string ToString() => Bruto;
    }

    public sealed record Texto(string Quanto) : Valor
    {
        public override string ToString() => Escritor.Escrever(this);
    }

    public sealed record Vetor(IReadOnlyList<Valor> Itens) : Valor
    {
        public override string ToString() => Escritor.Escrever(this);
    }

    /// <summary>
    /// Um objeto, com os pares na ordem em que apareceram.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A ordem é guardada de propósito, e a especificação diz que ela não
    /// importa. Não importa para o <i>significado</i> — e importa muito para
    /// quem vai reescrever o arquivo: um formatador que embaralhe as chaves
    /// produz uma diferença gigante num controle de versão por nada.
    /// </para>
    /// <para>
    /// As chaves repetidas também são guardadas. A RFC 8259 diz que o
    /// comportamento é "indefinido" quando um nome se repete, e quase toda
    /// biblioteca escolhe silenciosamente a última. Isso já foi vulnerabilidade
    /// de verdade: um sistema valida a primeira e outro usa a última, e um
    /// <c>{"admin": false, "admin": true}</c> passa pelos dois.
    /// </para>
    /// </remarks>
    public sealed record Objeto(IReadOnlyList<(string Nome, Valor Valor)> Pares) : Valor
    {
        public Valor? this[string nome]
        {
            get
            {
                // A última vence, que é o que o System.Text.Json faz. A
                // diferença é que aqui as outras continuam ali, para quem
                // quiser perguntar.
                Valor? achado = null;

                foreach (var (chave, valor) in Pares)
                {
                    if (chave == nome)
                    {
                        achado = valor;
                    }
                }

                return achado;
            }
        }

        public bool TemNomeRepetido()
        {
            var vistos = new HashSet<string>(StringComparer.Ordinal);

            foreach (var (nome, _) in Pares)
            {
                if (!vistos.Add(nome))
                {
                    return true;
                }
            }

            return false;
        }

        public override string ToString() => Escritor.Escrever(this);
    }

    // -- açúcar para montar valores na mão -------------------------------

    public static Valor De(string texto) => new Texto(texto);

    public static Valor De(bool quanto) => new Booleano(quanto);

    public static Valor De(double quanto) =>
        new Numero(quanto.ToString("R", CultureInfo.InvariantCulture));

    public static Valor De(long quanto) =>
        new Numero(quanto.ToString(CultureInfo.InvariantCulture));

    public static readonly Valor Nada = Nulo.Unico;
}

/// <summary>O erro, com a linha e a coluna.</summary>
/// <remarks>
/// Linha e coluna, e não só a posição do byte. Um arquivo de configuração de
/// duzentas linhas com um erro no byte 4.812 não ajuda ninguém; "linha 137,
/// coluna 9" ajuda, e é o que qualquer editor espera receber.
/// </remarks>
public sealed class ErroDeJson : Exception
{
    public int Posicao { get; }

    public int Linha { get; }

    public int Coluna { get; }

    public ErroDeJson(string mensagem, int posicao, int linha, int coluna)
        : base($"linha {linha}, coluna {coluna}: {mensagem}")
    {
        Posicao = posicao;
        Linha = linha;
        Coluna = coluna;
    }

    /// <summary>Mostra a linha do erro com um acento circunflexo embaixo.</summary>
    public string Apontar(string documento)
    {
        var linhas = documento.Split('\n');

        if (Linha - 1 >= linhas.Length || Linha < 1)
        {
            return Message;
        }

        var texto = new StringBuilder();

        texto.AppendLine(Message);
        texto.AppendLine("  " + linhas[Linha - 1].Replace("\t", " "));
        texto.Append("  " + new string(' ', Math.Max(0, Coluna - 1)) + '^');

        return texto.ToString();
    }
}

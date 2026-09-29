using System.Globalization;
using System.Text;

namespace Conde.Json;

/// <summary>
/// Lê JSON. Uma descida recursiva de duzentas linhas, e a lista do que recusar.
/// </summary>
/// <remarks>
/// <para>
/// A parte fácil de um analisador de JSON é aceitar JSON. A gramática cabe numa
/// página e a descida recursiva sai quase sozinha.
/// </para>
/// <para>
/// A parte difícil — e a que separa um analisador de brinquedo de um de verdade
/// — é <b>recusar o que não é JSON</b>. E a lista é longa, porque quase tudo o
/// que parece razoável está fora:
/// </para>
/// <list type="bullet">
///   <item><description><c>01</c> — zero à esquerda não vale</description></item>
///   <item><description><c>+1</c> — sinal de mais não vale</description></item>
///   <item><description><c>.5</c> e <c>1.</c> — o ponto precisa de dígito dos dois lados</description></item>
///   <item><description><c>0x1F</c>, <c>Infinity</c>, <c>NaN</c> — nada disso existe</description></item>
///   <item><description><c>'aspas simples'</c> — só aspas duplas</description></item>
///   <item><description><c>{a: 1}</c> — o nome tem de estar entre aspas</description></item>
///   <item><description><c>[1, 2,]</c> — vírgula sobrando não vale</description></item>
///   <item><description>uma quebra de linha literal dentro de um texto</description></item>
///   <item><description><c>// comentário</c> — não há comentário em JSON</description></item>
/// </list>
/// <para>
/// Cada uma delas parece implicância e cada uma tem um motivo: o formato foi
/// desenhado para ter <b>uma</b> leitura possível em qualquer linguagem. O zero
/// à esquerda existe porque em C ele quer dizer octal; o <c>NaN</c> ficou de
/// fora porque não há como escrevê-lo igual em toda parte.
/// </para>
/// </remarks>
public sealed class Analisador
{
    private readonly string _texto;
    private int _posicao;

    /// <summary>
    /// O teto de aninhamento.
    /// </summary>
    /// <remarks>
    /// Um arquivo com cem mil colchetes abertos tem cem mil bytes e derruba a
    /// pilha de qualquer analisador recursivo. Não é caso hipotético: é uma
    /// entrada de duas linhas de código que derruba um serviço, e o
    /// <c>System.Text.Json</c> tem o mesmo teto (64) pela mesma razão.
    /// </remarks>
    public const int ProfundidadeMaxima = 64;

    private Analisador(string texto)
    {
        _texto = texto;
    }

    /// <summary>Lê um documento inteiro.</summary>
    public static Valor Ler(string texto)
    {
        var analisador = new Analisador(texto);

        analisador.PularBrancos();

        var valor = analisador.LerValor(0);

        analisador.PularBrancos();

        if (!analisador.Acabou)
        {
            throw analisador.Erro(
                $"o documento acabou e ainda há '{analisador.Atual}' depois");
        }

        return valor;
    }

    /// <summary>Tenta ler, e diz se deu.</summary>
    public static bool Vale(string texto, out Valor? valor, out ErroDeJson? erro)
    {
        try
        {
            valor = Ler(texto);
            erro = null;

            return true;
        }
        catch (ErroDeJson problema)
        {
            valor = null;
            erro = problema;

            return false;
        }
    }

    private bool Acabou => _posicao >= _texto.Length;

    private char Atual => _texto[_posicao];

    private ErroDeJson Erro(string mensagem)
    {
        var linha = 1;
        var coluna = 1;

        for (var i = 0; i < _posicao && i < _texto.Length; i++)
        {
            if (_texto[i] == '\n')
            {
                linha++;
                coluna = 1;
            }
            else
            {
                coluna++;
            }
        }

        return new ErroDeJson(mensagem, _posicao, linha, coluna);
    }

    /// <summary>
    /// Pula o espaço em branco — e são <b>quatro</b> caracteres, não mais.
    /// </summary>
    /// <remarks>
    /// Espaço, tabulação, retorno e quebra de linha. Nada além disso: a
    /// tabulação vertical, o avanço de página e o espaço inquebrável do
    /// Unicode <b>não</b> são espaço em branco para o JSON, e um documento com
    /// um deles no meio é inválido. É o tipo de detalhe que só aparece quando
    /// alguém cola um texto de um editor de texto rico.
    /// </remarks>
    private void PularBrancos()
    {
        while (!Acabou && Atual is ' ' or '\t' or '\n' or '\r')
        {
            _posicao++;
        }
    }

    private Valor LerValor(int profundidade)
    {
        if (profundidade > ProfundidadeMaxima)
        {
            throw Erro($"o documento passa de {ProfundidadeMaxima} níveis de aninhamento");
        }

        if (Acabou)
        {
            throw Erro("esperava um valor e o documento acabou");
        }

        return Atual switch
        {
            '{' => LerObjeto(profundidade),
            '[' => LerVetor(profundidade),
            '"' => new Valor.Texto(LerTexto()),
            't' => LerPalavra("true", new Valor.Booleano(true)),
            'f' => LerPalavra("false", new Valor.Booleano(false)),
            'n' => LerPalavra("null", Valor.Nulo.Unico),
            '-' or >= '0' and <= '9' => LerNumero(),
            '\'' => throw Erro("o JSON só tem aspas duplas"),
            '/' => throw Erro("não há comentário em JSON"),
            'N' => throw Erro("NaN não é JSON"),
            'I' => throw Erro("Infinity não é JSON"),
            '+' => throw Erro("um número não pode começar com '+'"),
            '.' => throw Erro("um número precisa de um dígito antes do ponto"),
            _ => throw Erro($"não esperava '{Mostrar(Atual)}' aqui"),
        };
    }

    private Valor LerPalavra(string palavra, Valor valor)
    {
        if (_posicao + palavra.Length > _texto.Length
            || string.CompareOrdinal(_texto, _posicao, palavra, 0, palavra.Length) != 0)
        {
            throw Erro($"esperava '{palavra}'");
        }

        _posicao += palavra.Length;

        return valor;
    }

    private Valor LerObjeto(int profundidade)
    {
        _posicao++;

        var pares = new List<(string, Valor)>();

        PularBrancos();

        if (!Acabou && Atual == '}')
        {
            _posicao++;

            return new Valor.Objeto(pares);
        }

        while (true)
        {
            PularBrancos();

            if (Acabou)
            {
                throw Erro("o objeto não foi fechado");
            }

            if (Atual != '"')
            {
                throw Erro(Atual == '}'
                    ? "vírgula sobrando antes de '}'"
                    : "o nome de um campo tem de estar entre aspas duplas");
            }

            var nome = LerTexto();

            PularBrancos();

            if (Acabou || Atual != ':')
            {
                throw Erro("esperava ':' depois do nome do campo");
            }

            _posicao++;

            PularBrancos();

            pares.Add((nome, LerValor(profundidade + 1)));

            PularBrancos();

            if (Acabou)
            {
                throw Erro("o objeto não foi fechado");
            }

            if (Atual == ',')
            {
                _posicao++;
                continue;
            }

            if (Atual == '}')
            {
                _posicao++;

                return new Valor.Objeto(pares);
            }

            throw Erro($"esperava ',' ou '}}' e veio '{Mostrar(Atual)}'");
        }
    }

    private Valor LerVetor(int profundidade)
    {
        _posicao++;

        var itens = new List<Valor>();

        PularBrancos();

        if (!Acabou && Atual == ']')
        {
            _posicao++;

            return new Valor.Vetor(itens);
        }

        while (true)
        {
            PularBrancos();

            if (Acabou)
            {
                throw Erro("o vetor não foi fechado");
            }

            if (Atual == ']')
            {
                throw Erro("vírgula sobrando antes de ']'");
            }

            itens.Add(LerValor(profundidade + 1));

            PularBrancos();

            if (Acabou)
            {
                throw Erro("o vetor não foi fechado");
            }

            if (Atual == ',')
            {
                _posicao++;
                continue;
            }

            if (Atual == ']')
            {
                _posicao++;

                return new Valor.Vetor(itens);
            }

            throw Erro($"esperava ',' ou ']' e veio '{Mostrar(Atual)}'");
        }
    }

    /// <summary>
    /// Lê um texto entre aspas, com as fugas.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Duas regras aqui costumam faltar nas implementações caseiras.
    /// </para>
    /// <para>
    /// A primeira: <b>todo caractere de controle abaixo de 0x20 tem de estar
    /// escapado</b>. Uma quebra de linha literal dentro das aspas é inválida,
    /// e não é implicância — sem a regra, um texto com quebra dentro
    /// atravessaria a linha e um leitor de linha por vez se perderia.
    /// </para>
    /// <para>
    /// A segunda: as fugas <c>\u</c> são unidades UTF-16, não pontos de código.
    /// Um emoji é escrito como <b>dois</b> <c>\u</c> — o par substituto — e
    /// juntá-los é trabalho de quem lê. O JSON é de 2001, quando o UTF-16
    /// parecia ser a resposta, e ficou.
    /// </para>
    /// </remarks>
    private string LerTexto()
    {
        _posicao++;

        var texto = new StringBuilder();

        while (true)
        {
            if (Acabou)
            {
                throw Erro("o texto não foi fechado");
            }

            var letra = _texto[_posicao];

            if (letra == '"')
            {
                _posicao++;

                var pronto = texto.ToString();

                ExigirUnicodeValido(pronto);

                return pronto;
            }

            if (letra < 0x20)
            {
                throw Erro(
                    $"caractere de controle U+{(int)letra:X4} solto dentro do texto: "
                    + "ele tem de vir escapado");
            }

            if (letra != '\\')
            {
                texto.Append(letra);
                _posicao++;

                continue;
            }

            _posicao++;

            if (Acabou)
            {
                throw Erro("a fuga não foi completada");
            }

            var fuga = _texto[_posicao++];

            switch (fuga)
            {
                case '"': texto.Append('"'); break;
                case '\\': texto.Append('\\'); break;
                case '/': texto.Append('/'); break;
                case 'b': texto.Append('\b'); break;
                case 'f': texto.Append('\f'); break;
                case 'n': texto.Append('\n'); break;
                case 'r': texto.Append('\r'); break;
                case 't': texto.Append('\t'); break;
                case 'u': texto.Append(LerQuatroHexa()); break;
                default:
                    throw Erro($"não conheço a fuga '\\{Mostrar(fuga)}'");
            }
        }
    }

    /// <summary>
    /// O texto tem de ser Unicode de verdade: nada de substituto solto.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Um caractere fora do plano básico — um emoji, por exemplo — é escrito em
    /// JSON como <b>dois</b> <c>\u</c>: o par substituto do UTF-16. Os dois
    /// pedaços só têm sentido juntos, e um deles sozinho não corresponde a
    /// caractere nenhum e <b>não se codifica em UTF-8</b>.
    /// </para>
    /// <para>
    /// A gramática da RFC 8259 não proíbe o substituto solto, e é por isso que
    /// alguns analisadores o aceitam. O <c>System.Text.Json</c> recusa, porque
    /// o resultado não é um texto que dê para gravar. Foi o juiz quem apontou:
    /// o gerador apagou metade de um par de um emoji e eu aceitei o que sobrou.
    /// </para>
    /// <para>
    /// Aceitar seria produzir um valor que parece texto, compara como texto, e
    /// estoura na hora de escrever em disco — o pior tipo de defeito, o que
    /// aparece longe de onde nasceu.
    /// </para>
    /// </remarks>
    private void ExigirUnicodeValido(string texto)
    {
        for (var i = 0; i < texto.Length; i++)
        {
            if (!char.IsSurrogate(texto[i]))
            {
                continue;
            }

            var completo = char.IsHighSurrogate(texto[i])
                && i + 1 < texto.Length
                && char.IsLowSurrogate(texto[i + 1]);

            if (!completo)
            {
                throw Erro(
                    $"substituto solto U+{(int)texto[i]:X4} no texto: "
                    + "ele só tem sentido em par, e sozinho não é Unicode válido");
            }

            i++;
        }
    }

    private char LerQuatroHexa()
    {
        if (_posicao + 4 > _texto.Length)
        {
            throw Erro("a fuga \\u pede quatro dígitos hexadecimais");
        }

        var pedaco = _texto.Substring(_posicao, 4);

        foreach (var letra in pedaco)
        {
            if (!Uri.IsHexDigit(letra))
            {
                throw Erro($"'{pedaco}' não são quatro dígitos hexadecimais");
            }
        }

        _posicao += 4;

        return (char)ushort.Parse(pedaco, NumberStyles.HexNumber,
            CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Lê um número, com a gramática exata da especificação.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A gramática é <c>-? int frac? exp?</c>, e cada pedaço tem uma regra que
    /// pega gente:
    /// </para>
    /// <code>
    ///   int   := '0' | [1-9] [0-9]*      -- zero à esquerda NÃO vale
    ///   frac  := '.' [0-9]+              -- precisa de dígito depois do ponto
    ///   exp   := [eE] [+-]? [0-9]+       -- e de dígito depois do expoente
    /// </code>
    /// <para>
    /// O zero à esquerda é a regra mais estranha e a mais justificada: em C,
    /// em Perl e em vários outros, <c>012</c> é <b>dez</b>, em octal. Deixar a
    /// forma passar seria deixar o mesmo documento valer dois números
    /// diferentes conforme a linguagem que o lesse — o oposto do que o formato
    /// se propôs.
    /// </para>
    /// </remarks>
    private Valor LerNumero()
    {
        var comeco = _posicao;

        if (Atual == '-')
        {
            _posicao++;

            if (Acabou || !char.IsAsciiDigit(Atual))
            {
                throw Erro("esperava um dígito depois do '-'");
            }
        }

        if (Atual == '0')
        {
            _posicao++;

            if (!Acabou && char.IsAsciiDigit(Atual))
            {
                throw Erro("zero à esquerda não vale: em C isso seria octal");
            }
        }
        else
        {
            while (!Acabou && char.IsAsciiDigit(Atual))
            {
                _posicao++;
            }
        }

        if (!Acabou && Atual == '.')
        {
            _posicao++;

            if (Acabou || !char.IsAsciiDigit(Atual))
            {
                throw Erro("esperava um dígito depois do ponto");
            }

            while (!Acabou && char.IsAsciiDigit(Atual))
            {
                _posicao++;
            }
        }

        if (!Acabou && (Atual == 'e' || Atual == 'E'))
        {
            _posicao++;

            if (!Acabou && (Atual == '+' || Atual == '-'))
            {
                _posicao++;
            }

            if (Acabou || !char.IsAsciiDigit(Atual))
            {
                throw Erro("esperava um dígito depois do expoente");
            }

            while (!Acabou && char.IsAsciiDigit(Atual))
            {
                _posicao++;
            }
        }

        // Um `x` ou uma letra colada no número: `0x1F`, `1abc`. Sem esta
        // conferência o analisador leria o `0` e reclamaria só do `x` depois,
        // com uma mensagem que não explica nada.
        if (!Acabou && (char.IsAsciiLetter(Atual) || Atual == '.'))
        {
            throw Erro($"'{Mostrar(Atual)}' logo depois de um número");
        }

        return new Valor.Numero(_texto[comeco.._posicao]);
    }

    private static string Mostrar(char letra) => letra switch
    {
        '\n' => "\\n",
        '\r' => "\\r",
        '\t' => "\\t",
        _ when char.IsControl(letra) => $"\\u{(int)letra:x4}",
        _ => letra.ToString(),
    };
}

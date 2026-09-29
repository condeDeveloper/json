# json

Um analisador e um escritor de JSON do zero em C# e .NET 8, sem dependência
nenhuma.

```
$ dotnet json.dll conferir configuracao.json
linha 3, coluna 13: zero à esquerda não vale: em C isso seria octal
    "idade": 01
              ^
```

## O juiz

O `System.Text.Json` do próprio .NET, e **nos dois sentidos**:

| o que se prova | resultado |
|---|---|
| o que ele aceita, eu aceito | 30.000 documentos válidos, **100,000%** |
| o que ele recusa, eu recuso | 30.000 documentos estragados, **100,000%** |
| os valores lidos são os mesmos | 20.000 comparações, campo por campo |
| escrever e ler de volta dá o mesmo | 20.000 idas e voltas, nos três estilos |

A segunda linha é a que vale. Aceitar JSON válido é a parte fácil — a gramática
cabe numa página e a descida recursiva sai quase sozinha. **Recusar o que não é
JSON** é o trabalho todo, e é a metade que quase ninguém testa: o analisador
funciona com todo arquivo que o autor experimentou, e um dia lê `{a:1}` sem
reclamar.

O gerador de documentos estragados é o que cobre isso. Ele pega um documento
válido e o estraga de oito jeitos — troca um caractere, apaga um, repete um,
corta no meio, cola pontuação, põe lixo no fim, enfia um espaço que não é
espaço, deixa uma vírgula sobrando —, e a maioria dos estragos produz algo que
**continua parecendo JSON de longe**. Cada um é uma chance de ser permissivo
demais.

## O que o juiz pegou

**Um substituto de UTF-16 solto.**

Um caractere fora do plano básico — um emoji, por exemplo — é escrito em JSON
como **dois** `\u`: o par substituto. Os dois pedaços só têm sentido juntos, e
um deles sozinho não corresponde a caractere nenhum e **não se codifica em
UTF-8**.

A gramática da RFC 8259 não proíbe o substituto solto, e é por isso que alguns
analisadores o aceitam. O `System.Text.Json` recusa, porque o resultado não é um
texto que dê para gravar. O gerador apagou metade de um par de um emoji, eu
aceitei o que sobrou, e o juiz apontou — 81 divergências em 10.000.

Aceitar seria produzir um valor que parece texto, compara como texto, e estoura
na hora de escrever em disco: o pior tipo de defeito, o que aparece longe de
onde nasceu.

## O que parece JSON e não é

Cada uma destas linhas é recusada, e cada uma tem um motivo:

| o que | por que |
|---|---|
| `01` | em C, `012` é **dez**, em octal. O mesmo texto valeria dois números |
| `+1` | o sinal de mais não existe na gramática |
| `.5` e `1.` | o ponto precisa de dígito dos dois lados |
| `0x1F` | não há hexadecimal |
| `NaN`, `Infinity` | não há como escrevê-los igual em toda linguagem |
| `'aspas simples'` | só aspas duplas |
| `{a: 1}` | o nome do campo tem de estar entre aspas |
| `[1, 2,]` | vírgula sobrando |
| uma quebra de linha literal dentro de um texto | atravessaria a linha |
| `// comentário` | não há comentário em JSON |
| `1 2` e `{} {}` | um documento é **um** valor |

Cada uma parece implicância e cada uma tem a mesma razão de fundo: o formato foi
desenhado para ter **uma** leitura possível em qualquer linguagem. Douglas
Crockford descreveu o JSON num cartão de visita em 2001, e a lista do que ele
deixou de fora é mais interessante que a do que ele pôs.

**E o espaço em branco são quatro caracteres, não mais**: espaço, tabulação,
retorno e quebra de linha. A tabulação vertical, o avanço de página e o espaço
inquebrável do Unicode não são espaço em branco para o JSON — e um documento com
um deles no meio é inválido. É o tipo de detalhe que só aparece quando alguém
cola um texto de um editor rico.

## O número guarda o texto original

```csharp
var valor = Analisador.Ler("1e400");

((Valor.Numero)valor).Bruto     // "1e400"
((Valor.Numero)valor).Quanto    // +∞
```

O JSON não diz quantos bits um número tem, nem se é binário ou decimal: diz só a
sintaxe. Um `1e400` é JSON válido e não cabe num `double`; um
`123456789012345678901234567890` é JSON válido e não cabe num `long`.

Guardar o texto ao lado do `double` é a única forma de não mentir — e é o que
faz um documento atravessar este código e sair **idêntico** ao que entrou. Quem
só quer contas usa o `double`; quem vai reescrever o arquivo usa o texto.

## As chaves repetidas ficam

```
$ dotnet json.dll conferir pedido.json
JSON válido: 66 byte(s), 66 comprimido.
AVISO: 1 objeto(s) com nome de campo repetido.
profundidade: 3
```

A RFC 8259 diz que o comportamento é "indefinido" quando um nome se repete, e
quase toda biblioteca escolhe silenciosamente a última. Aqui a última também
vence no acesso por nome — porque é o que o `System.Text.Json` faz —, e as
outras **continuam ali**, para quem quiser perguntar.

Isso já foi vulnerabilidade de verdade: um sistema valida a primeira chave e
outro usa a última, e um `{"admin": false, "admin": true}` passa pelos dois.

## O aninhamento tem teto

Cem mil colchetes abertos são cem mil bytes e derrubam a pilha de qualquer
analisador recursivo. Não é caso hipotético — é uma entrada de duas linhas de
código que derruba um serviço. O teto é 64 níveis, o mesmo do
`System.Text.Json`, e pela mesma razão.

## O que escapar ao escrever

A regra mínima é curta: aspas, barra invertida e tudo abaixo de `0x20`. Quase
toda biblioteca escapa mais, e não é exagero — o `System.Text.Json`, por padrão,
escapa também `<`, `>`, `&`, `'` e `+`, para que o JSON possa ser colado dentro
de um `<script>` sem fechar a etiqueta. O clássico `</script>` dentro de um
texto já derrubou muita página e abriu muito XSS.

Aqui há os três estilos, e o que não dá é escolher sem saber que a escolha
existe:

```
json formatar   com recuo
json comprimir  sem um espaço sobrando
json paraPagina seguro para colar dentro de <script>
```

## Os comandos

```
json formatar   [ARQUIVO]   com recuo
json comprimir  [ARQUIVO]   sem um espaço sobrando
json paraPagina [ARQUIVO]   seguro para colar dentro de <script>
json conferir   [ARQUIVO]   é JSON? e o que há de estranho nele
json olhar      [ARQUIVO]   a árvore, campo por campo
```

Sem arquivo, lê a entrada padrão. O código de saída é 2 quando o documento não é
JSON — e a mensagem traz a linha, a coluna, e a linha do arquivo com um acento
circunflexo embaixo do caractere. Quando um arquivo de configuração de duzentas
linhas não carrega, "JSON inválido" não ajuda ninguém.

## Rodar

.NET 8. Zero dependências fora do xUnit, e só nos testes.

```
dotnet test testes/Json.Testes/Json.Testes.csproj
dotnet run --project ferramentas/Placar/Placar.csproj -- 20000
```

## O que ele não faz

Não lê em fluxo: o documento inteiro vai para a memória, o que é a escolha certa
para um arquivo de configuração e a errada para um de dez gigabytes. Não
converte para objetos de C# — não há serialização, só a árvore. Não tem JSON
Pointer, JSON Patch, JSON Schema, nem JSON Lines. E não é mais rápido que o
`System.Text.Json`, que tem anos de otimização, leitura direta de bytes UTF-8 e
caminhos vetorizados; aqui a troca é ler um `string` e ter um erro que diz onde.

## Licença

MIT.

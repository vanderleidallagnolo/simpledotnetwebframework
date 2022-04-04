using System;
using System.IO;
using System.Text;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using System.Collections.Generic;

class ServidorHttp
{

    /*
        Propriedade responsável por ficar escutando uma porta de rede do computador 
        à espera de qualquer tipo de conexão TCP
    */
    private TcpListener Controlador { get; set; }

    /* 
        Mantém o número da porta que será escutada
        Neste caso, vamos usar por padrão a porta 8080.
    */ 
    private int Porta { get; set; }

    /*  Contador que servirá para verificar se alguma conexão está sendo perdida,
        por exemplo.
    */
    private int QtdeRequests { get; set; }

    public string HtmlExemplo {get; set; }

    private SortedList<string,string> TiposMime {get; set; }

    private SortedList<string, string> DiretoriosHosts {get; set;}

    public ServidorHttp(int porta = 8080)
    {
        this.Porta = porta;
        this.CriarHtmlExemplo();
        this.PopularTiposMIME();
        this.PopularDiretoriosHosts();


        try
        {
            // Criando novo objeto TcpListener que vai escutar no IP 127.0.0.1 - local desta máquina
            // que vai escutar na porta this.Porta
            this.Controlador = new TcpListener(IPAddress.Parse("127.0.0.1"), this.Porta);
            this.Controlador.Start(); // inicia escuta na porta 8080

            // Informa ao usuário em qual porta o servidor está rodando
            Console.WriteLine($"Servidor HTTP está rodando na porta {this.Porta}.");

            // Informa ao usuário como acessar no navegador
            // localhost está mapeado -  por padrão - para o IP ("127.0.0.1")
            Console.WriteLine($"Para acessar, digite no navegador: http://localhost:{this.Porta}.");

            Task servidorHttpTask = Task.Run(() => AguardarRequest());

            // Informa ao programa para aguardar o término do método AguardarRequest()
            servidorHttpTask.GetAwaiter().GetResult();

        }
        catch (Exception e)
        {

                // Linha que mostra uma mensagem em caso de erro
                Console.WriteLine($"Erro ao iniciar servidor na porta {this.Porta}:\n{e.Message}");

        } // end try catch

    } // end public ServidorHttp(int porta = 8080)

    private async Task AguardarRequest()
    {
        while (true)
        {

            // Quando detecta a chegada de uma nova requisição retorna um objeto do tipo Socket
            // O objecto conexao - Socket - contém os dados da requisição e permite devolver uma resposta para o requisitante
            // que é o navegador do usuário, nesse caso.
            Socket conexao = await this.Controlador.AcceptSocketAsync();
            this.QtdeRequests++; // assim que aceita a requisição, a quantidade é incrementada e aguarda pela nova requisição

            // distribui ProcessarRequest a algum núcleo de processamento
            // este processamento vai acontecer de forma paralela ao processamento principal
            // Deixa o processamento mais robusto porque consegue verificar a chegada de nova conexão
            // enquanto processa a anterior
            Task task = Task.Run(() => ProcessarRequest(conexao, this.QtdeRequests));
        }

    } // end private async Task AguardarRequest()

    private void ProcessarRequest(Socket conexao, int numeroRequest)
    {

        Console.WriteLine($"Processando request #{numeroRequest}...\n");
        if (conexao.Connected) // se a conexão está CONECTADA
        {
            // Espaço em memória que armazena os dados da requisição
            byte[] bytesRequisicao = new byte[1024];

            // preenche o vetor de bytes com os dados recebidos do navegador do usuário
            // bytesRequisicao => onde guardar
            // bytesRequisicao.Length => quanto quero ler
            // 0 => a partir de que posição
            conexao.Receive(bytesRequisicao, bytesRequisicao.Length, 0);

            // Pegando os bytesRequisicao - lido atráves da conexao (Socket) - 
            // convertendo eles para o formato UTF8.
            // Depois de converter substitui o caracter correspondente ao número 0 por espaço
            // - em Replace((char)0, ' ').
            // E por fim, removo os espaços em Trim().
            // Essa operação é necessária porque bytesRequisicao é iniciado preenchido com 0 (zeros).
            // E todos os espaços não preenchidos continuam com 0 (zeros) e queremos eliminar estes 0 (zeros) adicionais.
            // Geralmente, a requisição ocupa menos do que os 1024 bytes.
            string TextoRequisicao = Encoding.UTF8.GetString(bytesRequisicao).Replace((char)0, ' ').Trim();

            // verifica se o texto da requisição é maior do que zero - se contém caracteres
            if (TextoRequisicao.Length > 0)
            {
                // mostra texto da requisição - o que está chegando no servidor
                // o texto mostrado é a requisição Http sendo feita pelo navegador do usuário
                Console.WriteLine($"\n{TextoRequisicao}\n");
                Console.WriteLine($"\nAQUI 1- Após o TextoRequisicao\n");


                string[] linhas = TextoRequisicao.Split("\r\n");
                 // captura a posição do primeiro caractere de espaço na primeira linha do texto da requisição
                int iPrimeiroEspaco = linhas[0].IndexOf(' ');
                // captura a posição do segundo caractere de espaço na primeira linha do texto da requisição
                int iSegundoEspaco = linhas[0].LastIndexOf(' ');
                string metodoHttp = linhas[0].Substring(0, iPrimeiroEspaco);
                string recursoBuscado = linhas[0].Substring(iPrimeiroEspaco + 1, iSegundoEspaco - iPrimeiroEspaco - 1);
                
                if (recursoBuscado == "/") recursoBuscado = "/index.html";

                string textoParametros = recursoBuscado.Contains("?") ? recursoBuscado.Split("?")[1] : "";
                SortedList<string, string> parametros = ProcessarParametros(textoParametros);

                recursoBuscado = recursoBuscado.Split("?")[0];

                string versaoHttp = linhas[0].Substring(iSegundoEspaco + 1);

                iPrimeiroEspaco = linhas[1].IndexOf(' ');
                string nomeHost = linhas[1].Substring(iPrimeiroEspaco + 1);

                byte[] bytesCabecalho = null;
                byte[] bytesConteudo = null; 

                // objeto completo para listar informações a respeito do arquivo
                FileInfo fiArquivo = new FileInfo(ObterCaminhoFisicoArquivo(nomeHost, recursoBuscado));

                Console.WriteLine($"\nAQUI 2 - {fiArquivo}\n");

                if (fiArquivo.Exists)
                {
                    // verificando se a lista de tipos MIME suportado contém a extensão do arquivo suportado pelo navegador
                    if (TiposMime.ContainsKey(fiArquivo.Extension.ToLower()))
                    {


                        if (fiArquivo.Extension.ToLower() == ".dhtml")
                        {
                            bytesConteudo = GerarHTMLDinamico(fiArquivo.FullName, parametros, metodoHttp );
                        }
                        else
                        {

                            bytesConteudo = File.ReadAllBytes(fiArquivo.FullName);
                        }

                        
                        string tipoMime = TiposMime[fiArquivo.Extension.ToLower()];


                        bytesCabecalho = GerarCabecalho(versaoHttp, tipoMime, "200", bytesConteudo.Length);

                    }
                    else // quando não há suporte para o tipo MIME enviado na requisição
                    {

                        bytesConteudo = Encoding.UTF8.GetBytes("<h1>Erro 415 - Tipo de arquivo não suportado.</h1>");
                        bytesCabecalho = GerarCabecalho(versaoHttp, "text/html; charset=utf-8", "415", bytesConteudo.Length);

                    } // if (TiposMime.ContainsKey(fiArquivo.Extension.ToLower()))

                }
                else
                {
                        bytesConteudo = Encoding.UTF8.GetBytes("<h1>Erro 404 - Arquivo Não Encontrado</h1>");
                        bytesCabecalho = GerarCabecalho(versaoHttp, "text, html; charset=utf-8","404", bytesConteudo.Length);
                }// end  if (fiArquivo.Exists)
                
                int bytesEnviados = conexao.Send(bytesCabecalho, bytesCabecalho.Length, 0);
                bytesEnviados += conexao.Send(bytesConteudo, bytesConteudo.Length, 0);
                conexao.Close();

                Console.WriteLine($"\n{bytesEnviados} bytes enviados em respostas à requsição #{numeroRequest}.");
            }
        
        } // end if (conexao.Connected) // se a conexão está CONECTADA
        Console.WriteLine($"\nRequest {numeroRequest} finalizado.");

    } // end private void ProcessarRequest(Socket conexao, int numeroRequest)

    public byte[] GerarCabecalho(string versaoHttp, string tipoMime, 
        string codigoHttp, int qtdeBytes = 0)
    {

        StringBuilder texto = new StringBuilder();
        texto.Append($"{versaoHttp} {codigoHttp}{Environment.NewLine}");
        texto.Append($"Server: Servidor Http Simples 1.0 {Environment.NewLine}");
        texto.Append($"Content-Type: {tipoMime}{Environment.NewLine}");
        texto.Append($"Content-Length: {qtdeBytes} {Environment.NewLine}{Environment.NewLine}");
        return Encoding.UTF8.GetBytes(texto.ToString());
        
    } // end public byte[] GerarCabecalho(string versaoHttp, string tipoMime,  string codigo Http, int qtdeBytes = 0)
    
    private void CriarHtmlExemplo()
    {
        StringBuilder html = new StringBuilder();
        html.Append("<!DOCTYPE html><html lang=\"pt-br\"><head><meta charset=\"UTF-8\">");
        html.Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1.0\">");
        html.Append("<title>Página Estática</title></head><body>");
        html.Append("<h1>Página Estática</h1></body></html>");
        this.HtmlExemplo = html.ToString();
    }

    // método que vai popular a lista de tipos MIME
    private void PopularTiposMIME()
    {

        this.TiposMime = new SortedList<string, string>();
        this.TiposMime.Add(".html"  , "text/html; charset=utf-8"    );
        this.TiposMime.Add(".htm"   , "text/html; charset=utf-8"    );
        this.TiposMime.Add(".css"   , "text/css"                    );
        this.TiposMime.Add(".js"    , "text/javascript"             );
        this.TiposMime.Add(".png"   , "image/png"                   );
        this.TiposMime.Add(".jpg"   , "image/jpg"                   );
        this.TiposMime.Add(".gif"   , "image/gif"                   );
        this.TiposMime.Add(".svg"   , "image/svg+xml"               );
        this.TiposMime.Add(".webp"  , "image/webp"                  );
        this.TiposMime.Add(".ico"   , "image/ico"                   );
        this.TiposMime.Add(".woff"  , "font/woff"                   );
        this.TiposMime.Add(".woff2" , "font/woff2"                  );
        this.TiposMime.Add(".dhtml" , "text/html; charset=utf-8"    );

    } // end private void PopularTiposMIME()

    private void PopularDiretoriosHosts()
    {

        this.DiretoriosHosts = new SortedList<string, string>();
        this.DiretoriosHosts.Add("localhost","C:\\VSCode\\simpledotnetwebframework\\www\\localhost");
        this.DiretoriosHosts.Add("maroquio.com","C:\\VSCode\\simpledotnetwebframework\\www\\maroquio.com");
        this.DiretoriosHosts.Add("quitandaonline.com.br","C:\\Youtube\\QuintandaOnline");

    } // end private void PopularDiretoriosHosts()

    public string ObterCaminhoFisicoArquivo(string host, string arquivo)
    {
        string diretorio = this.DiretoriosHosts[host.Split(":")[0]];
        string caminhoArquivo = diretorio + arquivo.Replace("/", "\\");
        return caminhoArquivo;
    }

    public byte[] GerarHTMLDinamico(string caminhoArquivo, SortedList<string, string> parametros, string metodoHttp)
    {

        FileInfo fiArquivo = new FileInfo(caminhoArquivo);
        string nomeClassePaginna = "Pagina" + fiArquivo.Name.Replace(fiArquivo.Extension, "");

        // INSTANCIANDO CLASSE A PARTIR DO NOME DELA - Begin

        // Obtém referência para o tipo da página a partir do nome dessa classe da página
        // - nomeClassePagina = nome da classe da página
        // -  true (primeiro) -> indicando lançar exceção caso seja inexiste essa classe
        // -  true (segundo) -> ignorar o caso (Case insensitive)
        Type tipoPaginaDinamica = Type.GetType(nomeClassePaginna, true, true);
        PaginaDinamica pd = Activator.CreateInstance(tipoPaginaDinamica) as PaginaDinamica;

        // alimentar o HtmlModelo
        pd.HtmlModelo = File.ReadAllText(caminhoArquivo);

        switch (metodoHttp.ToLower())
        {
            case "get":
                return pd.Get(parametros);
            case "post":
                return pd.Post(parametros);
            default:
                return new byte[0];
        }

        // END - INSTANCIANDO CLASSE A PARTIR DO NOME DELA

    } // end public byte[] GerarHTMLDinamico(string caminhoArquivo)

    private SortedList<string, string> ProcessarParametros(string textoParametros)
    {
        SortedList<string, string> parametros = new SortedList<string, string>();

        // testa para verificar se textoParametros é diferente de vazio
        // usando método Trim para remover quaisquer espaços à esquerda e à direita
        if (!string.IsNullOrEmpty(textoParametros.Trim()))
        {
            string[] paresChaveValor = textoParametros.Split("&");

            foreach (var par in paresChaveValor)
            {
                parametros.Add(par.Split("=")[0], par.Split("=")[1]);
            }
        }

        return parametros;
    }

} // end class ServidorHttp
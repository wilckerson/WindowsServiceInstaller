using FirebirdSql.Data.FirebirdClient;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SharpRaven;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using WindowsService.Helper;
using WindowsService.Storage;

namespace WindowsService.Integrations
{
    public class FormulaCertaIntegration
    {
        LogToFileHelper logToFile;

        readonly string connectionString;
        readonly string idFarmaciaIntegracao;
        readonly string integracaoOrcamentosUrl;
        readonly string integracaoFormulasUrl;
        readonly string autenticacaoUrl;
        readonly string autenticacaoUsuario;
        readonly string autenticacaoSenha;
        readonly int periodoCargaInicialOrcamentos;
        readonly int periodoCargaInicialFormulas;
        readonly int tamanhoPacote;
        readonly RavenClient sentry;

        string accessToken;

        static bool isRunning;

        //const string RootPath = "c:/pharmaRocketService";

        public FormulaCertaIntegration()
        {
            var logFilePath = System.Configuration.ConfigurationManager.AppSettings["logFilePath"];
            logToFile = new LogToFileHelper(logFilePath);

            string configTamanhoPacote = System.Configuration.ConfigurationManager.AppSettings["tamanhoPacote"];
            if (!int.TryParse(configTamanhoPacote, out tamanhoPacote))
            {
                tamanhoPacote = 300;
            }

            connectionString = System.Configuration.ConfigurationManager.AppSettings["connectionString"];
            idFarmaciaIntegracao = System.Configuration.ConfigurationManager.AppSettings["idFarmaciaIntegracao"];
            autenticacaoUsuario = System.Configuration.ConfigurationManager.AppSettings["autenticacaoUsuario"];
            autenticacaoSenha = System.Configuration.ConfigurationManager.AppSettings["autenticacaoSenha"];

            var sentryKey = System.Configuration.ConfigurationManager.AppSettings["sentryKey"];
            sentry = new RavenClient(sentryKey);

            var cargaInicialOrcamentoDias = System.Configuration.ConfigurationManager.AppSettings["cargaInicialOrcamentoDias"];
            if (!int.TryParse(cargaInicialOrcamentoDias,out periodoCargaInicialOrcamentos))
            {
                periodoCargaInicialOrcamentos = 60;
            }

            var cargaInicialFormulaDias = System.Configuration.ConfigurationManager.AppSettings["cargaInicialFormulaDias"];
            if (!int.TryParse(cargaInicialFormulaDias, out periodoCargaInicialFormulas))
            {
                periodoCargaInicialFormulas = 360;
            }

            var apiUrl = System.Configuration.ConfigurationManager.AppSettings["apiUrl"];
            integracaoOrcamentosUrl = $"{apiUrl}/{System.Configuration.ConfigurationManager.AppSettings["integracaoOrcamentosUrl"]}";
            integracaoFormulasUrl = $"{apiUrl}/{System.Configuration.ConfigurationManager.AppSettings["integracaoFormulasUrl"]}";
            autenticacaoUrl = $"{apiUrl}/{System.Configuration.ConfigurationManager.AppSettings["autenticacaoUrl"]}";
        }

        public void RunIntegracaoFormulas()
        {
            logToFile.Log(">>> Iniciando integração das formulas...");

            //Faz a leitura da data do ultimo item sincronizado 
            var jonStoragePath = System.Configuration.ConfigurationManager.AppSettings["jonStoragePath"];
            var model = JsonStorageHelper.Read<StorageModel>(jonStoragePath);

            //Se nao tiver data do ultimo item sincronizado, utiliza a data de 1 ano atrás
            var dataFormula = model.DtUltimaFormulaSincronizada.GetValueOrDefault(DateTime.UtcNow.AddDays(-1 * periodoCargaInicialFormulas).Date);

            //Obtem as formulas
            var lstFormulas = GetFormulas(dataFormula, model.IdsFormulaSincronizadasDoDia);

            if (lstFormulas.Any())
            {

                //Envia as formulas para API do PharmaRocket em varios pacotes menores
                PushPacotes(tamanhoPacote, lstFormulas, (
                    lstPacote => PushFormulas(idFarmaciaIntegracao, lstPacote)
                )).Wait();

                //Se der tudo certo no envio, salva a data do ultimo item sincronizado
                var dataUltimaFormula = lstFormulas
                    .Select(s => s.DataAlteracao)
                    .OrderByDescending(o => o)
                    .FirstOrDefault();

                //Como a data das formulas não possuem hora, armazena o ids das formulas sincronizadas no dia para não ficar enviando toda hora para a API
                var ids = lstFormulas
                    .Where(w => w.DataAlteracao.GetValueOrDefault().Date == dataUltimaFormula.GetValueOrDefault().Date)
                    .Select(s => s.Id.ToString())
                    .ToList();

                model.IdsFormulaSincronizadasDoDia = ids;
                model.DtUltimaFormulaSincronizada = dataUltimaFormula;
                JsonStorageHelper.Write(jonStoragePath, model);

                logToFile.Log(">>> Integração das formulas executada com sucesso.");
            }
            else
            {
                logToFile.Log(">>> Nenhuma formula para sincronizar.");
            }
        }

        void RunIntegracaoOrcamentos()
        {
            logToFile.Log(">>> Iniciando integração dos orçamentos...");

            //Faz a leitura da data do ultimo item sincronizado 
            var jonStoragePath = System.Configuration.ConfigurationManager.AppSettings["jonStoragePath"];
            var model = JsonStorageHelper.Read<StorageModel>(jonStoragePath);

            //Se nao tiver data do ultimo item sincronizado, utiliza a data de 60 dias atrás
            var dataOrcamento = model.DtUltimoOrcamentoSincronizado.GetValueOrDefault(DateTime.UtcNow.AddDays(-1 * periodoCargaInicialOrcamentos).Date);

            //Busca os registros mais recentes que data do ultimo item sincronizado
            var lstOrcamentos = GetOrcamentos(dataOrcamento);

            //Envia os registros para API do PharmaRocket em vários pacotes menores
            if (lstOrcamentos.Any())
            {
                PushPacotes(tamanhoPacote, lstOrcamentos, (
                    lstPacote => PushOrcamentos(idFarmaciaIntegracao, lstPacote)
                )).Wait();

                //Se der tudo certo no envio, salva a data do ultimo item sincronizado
                var dataUltimoOrcamento = lstOrcamentos
                    .Select(s => s.DataAlteracao)
                    .OrderByDescending(o => o)
                    .FirstOrDefault();

                model.DtUltimoOrcamentoSincronizado = dataUltimoOrcamento;
                JsonStorageHelper.Write(jonStoragePath, model);

                logToFile.Log(">>> Integração dos orçamentos executada com sucesso.");
            }
            else
            {
                logToFile.Log(">>> Nenhum orçamento para sincronizar.");
            }
        }

        public void Run()
        {
            if (isRunning)
            {
                return;
            }
            
            FbConnection connection = null;
            try
            {
                isRunning = true;

                logToFile.Log($"Serviço em execução.");

                //Autentica na API
                this.accessToken = Autenticacao().Result;

                RunIntegracaoFormulas();

                RunIntegracaoOrcamentos();

                isRunning = false;
                logToFile.Log($"Fim da execução.");

            }
            catch (Exception ex)
            {
                logToFile.Log($"Erro ao executar a integração. Exception: {ex.Message} {ex.InnerException?.Message}");
                isRunning = false;

                //Encerrando a conexão
                connection?.Close();
                connection?.Dispose();

                ex.Data.Add("idFarmaciaIntegracao", idFarmaciaIntegracao);
                sentry.Capture(new SharpRaven.Data.SentryEvent(ex));
            }
        }

        private FbConnection ConnectToLocalDatabase(string connectionString)
        {
            var connection = new FbConnection(connectionString);
            return connection;
        }

        private List<Dictionary<string, object>> GetDataRowsFromSql(string sqlQuery, string connectionString)
        {
            List<Dictionary<string, object>> dataList = new List<Dictionary<string, object>>();

            FbConnection connection = null;
            FbDataReader reader = null;
            FbCommand command = null;
            try
            {
                connection = new FbConnection(connectionString);
                connection.Open();

                command = new FbCommand(sqlQuery, connection);
                reader = command.ExecuteReader();

                while (reader.Read())
                {
                    var dataItem = new Dictionary<string, object>();
                    for (int i = 0; i < reader.FieldCount; i++)
                    {
                        var columnName = reader.GetName(i);
                        var columnValue = reader[i];
                        dataItem.Add(columnName, columnValue);
                    }
                    dataList.Add(dataItem);
                }
            }
            catch (Exception ex)
            {
                throw;
            }
            finally
            {
                //command?.Dispose();
                //reader?.Close();
                connection?.Close();
                connection?.Dispose();
            }

            return dataList;
        }

        private List<Orcamento> GetOrcamentos(DateTime data)
        {
            List<Orcamento> lstOrcamentos = new List<Orcamento>();

            var dtQuery = data.ToString("yyyy-MM-dd");
            var timeQuery = data.ToString("HH:mm:ss");

            List<Dictionary<string, object>> dbOrcamentos = new List<Dictionary<string, object>>();
            try
            {
                dbOrcamentos = GetDataRowsFromSql($@"
                    SELECT 
                        orcamento.ID,
                        orcamento.IDPEDIDO,
                        orcamento.DESCRICAOWEB,
                        orcamento.IDSTATUSITEMPEDIDO,
                        orcamento.IDTIPOITEMPEDIDO,
                        orcamento.QUANT,
                        orcamento.PRUNI,
                        orcamento.PTDSC,
                        orcamento.VRDSC,
                        orcamento.VRTXA,
                        orcamento.VRLIQ,
                        orcamento.VRTOT,
                        orcamento.NRORC,
                        orcamento.DTALT,
                        orcamento.HRALT
                        ,pedido.TITULOWEB,
                        cliente.CDCLI, cliente.NOMECLI, cliente.EMAIL 
                    FROM FC0M100 AS orcamento
                    LEFT JOIN FC0M000 AS pedido ON pedido.ID = orcamento.IDPEDIDO
                    LEFT JOIN FC07000 AS cliente ON cliente.CDCLI = pedido.CDCLIWEB
                    WHERE orcamento.DTALT >= '{dtQuery}' and orcamento.HRALT > '{timeQuery}'
                ", this.connectionString);
            }
            catch (Exception ex)
            {
                throw new Exception("Erro ao obter os dados da base.", ex);
            }

            try
            {
                foreach (var s in dbOrcamentos)
                {
                    var orc = new Orcamento();
                    orc.Id = int.Parse(s["ID"].ToString());
                    orc.IdPedido = int.Parse(s["IDPEDIDO"].ToString());
                    orc.Formula = s["DESCRICAOWEB"].ToString();
                    orc.IdStatusItem = int.Parse(s["IDSTATUSITEMPEDIDO"].ToString());
                    orc.IdTipoItem = int.Parse(s["IDTIPOITEMPEDIDO"].ToString());
                    orc.Quantidade = string.IsNullOrEmpty(s["QUANT"].ToString()) ? 0 : int.Parse(s["QUANT"].ToString());
                    orc.PrecoItem = string.IsNullOrEmpty(s["PRUNI"].ToString()) ? 0 : decimal.Parse(s["PRUNI"].ToString());
                    orc.PorcentagemDesconto = string.IsNullOrEmpty(s["PTDSC"].ToString()) ? 0 : double.Parse(s["PTDSC"].ToString());
                    orc.ValorDesconto = string.IsNullOrEmpty(s["VRDSC"].ToString()) ? 0 : decimal.Parse(s["VRDSC"].ToString());
                    orc.ValorTaxaEntrega = string.IsNullOrEmpty(s["VRTXA"].ToString()) ? 0 : decimal.Parse(s["VRTXA"].ToString());
                    orc.ValorTotalComDesconto = string.IsNullOrEmpty(s["VRLIQ"].ToString()) ? 0 : decimal.Parse(s["VRLIQ"].ToString());
                    orc.ValorTotalSemDesconto = string.IsNullOrEmpty(s["VRTOT"].ToString()) ? 0 : decimal.Parse(s["VRTOT"].ToString());
                    orc.NumOrcamento = s["NRORC"].ToString();
                    orc.TipoPedido = s["TITULOWEB"].ToString();
                    orc.DataAlteracao = DateTime.Parse($"{s["DTALT"].ToString().Substring(0, 10)} {s["HRALT"]}");
                    orc.Cliente = new Cliente();
                    orc.Cliente.Id = s["CDCLI"] != null ? int.Parse(s["ID"].ToString()) : new int?();
                    orc.Cliente.Nome = s["NOMECLI"].ToString();
                    orc.Cliente.Email = s["EMAIL"].ToString();

                    lstOrcamentos.Add(orc);
                }
            }
            catch (Exception ex)
            {
                throw new Exception("Erro ao converter os dados.", ex);
            }

            return lstOrcamentos;
        }

        private List<Formula> GetFormulas(DateTime data, List<string> idsFormulasIgnorar)
        {
            List<Formula> lstFormulas = new List<Formula>();

            var dtQuery = data.ToString("yyyy-MM-dd");

            string notInQuery = "0=0";
            if (idsFormulasIgnorar != null && idsFormulasIgnorar.Any())
            {
                var ids = string.Join(",", idsFormulasIgnorar);
                notInQuery = $" formula.CDFRM NOT IN ({ids})";
            }

            var dbItensFormula = GetDataRowsFromSql($@"
                SELECT
                    itemFormula.ITEMID,
                    itemFormula.CDFRM,
                    itemFormula.DESCR,
                    itemFormula.QUANT,
                    itemFormula.UNIDA,
                    itemFormula.INDQSP,
                    itemFormula.VRCMP,
                    formula.DTALT,
                    formula.DESCRFRM,
                    formula.VOLUME,
                    formula.UNIVOL,
                    formula.TPCAP,
                    formula.INDQSP as FRM_INDQSP
                FROM FC05100 AS itemFormula
                INNER JOIN FC05000 AS formula ON formula.CDFRM = itemFormula.CDFRM
                WHERE formula.DTALT >= '{dtQuery}' and {notInQuery}
            ", this.connectionString);

            //Agrupando os itemFormula por formula
            var grupoFormulas = dbItensFormula.GroupBy(g => g["CDFRM"]);

            foreach (var grupoFormula in grupoFormulas)
            {
                //Utiliza o primeiro item para montar o objeto formula
                var dbFormula = grupoFormula.FirstOrDefault();

                var formula = new Formula
                {
                    Id = int.Parse(dbFormula["CDFRM"].ToString()),
                    Descricao = dbFormula["DESCRFRM"].ToString(),
                    Volume = decimal.Parse(dbFormula["VOLUME"].ToString()),
                    UnidadeVolume = dbFormula["UNIVOL"].ToString(),
                    TamanhoCapsula = dbFormula["TPCAP"].ToString(),
                    Qsp = dbFormula["FRM_INDQSP"].ToString() == "S",
                    DataAlteracao = DateTime.Parse(dbFormula["DTALT"].ToString()),
                    ItensFormula = new List<ItemFormula>()
                };

                //Monta os objetos itensFormula e relaciona com o objeto formula
                foreach (var dbItemFormula in grupoFormula)
                {
                    var itemFormula = new ItemFormula()
                    {
                        IdItem = int.Parse(dbItemFormula["ITEMID"].ToString()),
                        IdFormula = int.Parse(dbItemFormula["CDFRM"].ToString()),
                        Descricao = dbItemFormula["DESCR"].ToString(),
                        Quantidade = decimal.Parse(dbFormula["QUANT"].ToString()),
                        Unidade = dbItemFormula["UNIDA"].ToString(),
                        Qsp = dbItemFormula["INDQSP"].ToString() == "S",
                        Valor = decimal.Parse(dbFormula["VRCMP"].ToString())
                    };

                    formula.ItensFormula.Add(itemFormula);
                }

                //Armazena no objeto de retorno
                lstFormulas.Add(formula);
            }

            return lstFormulas;
        }

        /// <summary>
        /// Enviando para o servidor em pacotes para evitar uma requisição com Json muito longo
        /// </summary>
        /// <param name="tamanhoPacote"></param>
        /// <param name="idFarmacia"></param>
        /// <param name="lst"></param>
        /// <returns></returns>
        private async Task PushPacotes<T>(int tamanhoPacote, IEnumerable<T> lst, Func<IEnumerable<T>, Task<string>> pushAction)
        {
            if (!lst.Any()) { return; }

            var qtdPacotes = (lst.Count() / tamanhoPacote) + 1;

            for (int i = 0; i < qtdPacotes; i++)
            {
                var lstPacote = lst.Skip(i * tamanhoPacote).Take(tamanhoPacote);

                logToFile.Log($"Enviando pacote {i + 1} de {qtdPacotes} com {lstPacote.Count()} itens");

                var responseString = await pushAction?.Invoke(lstPacote);

                logToFile.Log($"Pacote enviado. Resposta: {responseString}");
            }
        }

        private async Task<string> PushOrcamentos(string idFarmacia, IEnumerable<Orcamento> lstOrcamentos)
        {
            //return "mock";

            var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", this.accessToken);

            var jsonObject = JsonConvert.SerializeObject(lstOrcamentos);
            var content = new StringContent(jsonObject.ToString(), Encoding.UTF8, "application/json");

            var response = await httpClient.PostAsync(integracaoOrcamentosUrl + idFarmacia, content);

            var responseString = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                throw new Exception($"Erro ao enviar os orçamentos para a API PharmaRocket. Response: {response.StatusCode} {responseString}");
            }

            return responseString;
        }

        private async Task<string> PushFormulas(string idFarmacia, IEnumerable<Formula> lstFormulas)
        {
            //return "mock";

            var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", this.accessToken);

            var jsonObject = JsonConvert.SerializeObject(lstFormulas);
            var content = new StringContent(jsonObject.ToString(), Encoding.UTF8, "application/json");

            var response = await httpClient.PostAsync(integracaoFormulasUrl + idFarmacia, content);

            var responseString = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                throw new Exception($"Erro ao enviar as formulas para a API PharmaRocket. Response: {response.StatusCode} {responseString}");
            }

            return responseString;
        }

        private async Task<string> Autenticacao()
        {
            var httpClient = new HttpClient();
            var jsonObject = JsonConvert.SerializeObject(new { login = autenticacaoUsuario, senha = autenticacaoSenha });
            var content = new StringContent(jsonObject.ToString(), Encoding.UTF8, "application/json");
            var response = await httpClient.PostAsync(autenticacaoUrl, content);

            var responseString = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                throw new Exception($"Erro ao enviar as formulas para a API PharmaRocket. Response: {response.StatusCode} {responseString}");
            }

            var responseJson = JObject.Parse(responseString);
            var accessToken = responseJson["accessToken"].ToString();

            return accessToken;
        }
    }

    public class Orcamento
    {
        public int Id { get; set; }
        public int IdPedido { get; set; }
        public int IdStatusItem { get; set; }
        public int IdTipoItem { get; set; }
        public string Formula { get; set; }
        public int Quantidade { get; set; }
        public decimal PrecoItem { get; set; }
        public double PorcentagemDesconto { get; set; }
        public decimal ValorDesconto { get; set; }
        public decimal ValorTaxaEntrega { get; set; }
        public decimal ValorTotalComDesconto { get; set; }
        public decimal ValorTotalSemDesconto { get; set; }
        public string NumOrcamento { get; set; }
        public string TipoPedido { get; set; }
        public DateTime DataAlteracao { get; set; }

        public Cliente Cliente { get; set; }
    }

    public class Cliente
    {
        public int? Id { get; set; }
        public string Nome { get; set; }
        public string Email { get; set; }

        public int HashCode
        {
            get
            {
                unchecked // Overflow is fine, just wrap
                {
                    int hash = 17;
                    // Suitable nullity checks etc, of course :)
                    hash = hash * 23 + (Id != null ? Id.GetHashCode() : 0);
                    hash = hash * 23 + (Nome != null ? Nome.GetHashCode() : 0);
                    hash = hash * 23 + (Email != null ? Email.GetHashCode() : 0);
                    return hash;
                }
            }
        }
    }

    public class Formula
    {
        public int Id { get; set; }
        public string Descricao { get; set; }
        public decimal Volume { get; set; }
        public string UnidadeVolume { get; set; }
        public string TamanhoCapsula { get; set; }
        public bool? Qsp { get; set; }
        public List<ItemFormula> ItensFormula { get; set; }
        public DateTime? DataAlteracao { get; set; }
    }

    public class ItemFormula
    {
        public int IdItem { get; set; }
        public int IdFormula { get; set; }
        public string Descricao { get; set; }
        public decimal Quantidade { get; set; }
        public string Unidade { get; set; }
        public bool Qsp { get; set; }
        public decimal Valor { get; set; }
    }
}

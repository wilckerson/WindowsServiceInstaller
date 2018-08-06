using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WindowsService.Storage
{
    public class StorageModel
    {
        public DateTime? DtUltimoOrcamentoSincronizado { get; set; }
        public DateTime? DtUltimaFormulaSincronizada { get; set; }
        public List<string> IdsFormulaSincronizadasDoDia { get; set; }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PSIP
{
    class ArpTable
    {
        private List<ArpRow> table = new List<ArpRow>();

        internal List<ArpRow> Table
        {
            get
            {
                return table;
            }

            set
            {
                table = value;
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WANT
{
    class Ping
    {
        private ushort identifier;
        private ushort SequenceNumber;
        private DateTime time;


        public ushort Identifier
        {
            get
            {
                return identifier;
            }

            set
            {
                identifier = value;
            }
        }

        public ushort SequenceNumber1
        {
            get
            {
                return SequenceNumber;
            }

            set
            {
                SequenceNumber = value;
            }
        }

        public DateTime Time
        {
            get
            {
                return time;
            }

            set
            {
                time = value;
            }
        }
    }
}

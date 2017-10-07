using PcapDotNet.Core;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace PSIP
{
    public partial class Form1 : Form
    {
        private IList<LivePacketDevice> allDevices;
        private LivePacketDevice device,device2;
        public Form1()
        {
            InitializeComponent();
            allDevices = LivePacketDevice.AllLocalMachine;
            Console.WriteLine(allDevices.Count);
            for (int i = 0; i != allDevices.Count; ++i)
            {

                LivePacketDevice device = allDevices[i];
                if (device.Description != null)
                {
                    ListViewItem row = new ListViewItem(device.Description);
                    ListViewItem row2 = new ListViewItem(device.Description);
                    //row.SubItems.Add(device.Description);
                    listView1.HideSelection = false;
                    listView2.HideSelection = false;
                    listView1.Items.Add(row);
                    listView2.Items.Add(row2);
                }
            }
        }

        private void listView1_SelectedIndexChanged(object sender, EventArgs e)
        {
            
        }

        private void button1_Click(object sender, EventArgs e)
        {
            device = allDevices[listView1.SelectedItems[0].Index];
            device2 = allDevices[listView2.SelectedItems[0].Index];
            Console.Write("Opening: \n");
            Console.Write("1:" + allDevices[listView1.SelectedItems[0].Index].Description + "\n");
            Console.Write("2:" + allDevices[listView2.SelectedItems[0].Index].Description + "\n \n");
            var pkts = new Form2(device,device2);
            pkts.Closed += (s, args) => this.Close();
            pkts.Show();
            this.Hide();
      
        }
    }
}

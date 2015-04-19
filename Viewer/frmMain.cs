/*
	Copyright(c) 2015, [kaisar] @ rightcode.org
	All rights reserved.

	Redistribution and use in source and binary forms, with or without
	modification, are permitted provided that the following conditions are met :

	*Redistributions of source code must retain the above copyright notice, this
	list of conditions and the following disclaimer.

	* Redistributions in binary form must reproduce the above copyright notice,
	this list of conditions and the following disclaimer in the documentation
	and / or other materials provided with the distribution.

	* Neither the name of LinkedTracker nor the names of its
	contributors may be used to endorse or promote products derived from
	this software without specific prior written permission.

	THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS"
	AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE
	IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
	DISCLAIMED.IN NO EVENT SHALL THE COPYRIGHT HOLDER OR CONTRIBUTORS BE LIABLE
	FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL
	DAMAGES(INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR
	SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER
	CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY,
	OR TORT(INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE
	OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
*/
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace LinkedTrackerView
{
    public partial class frmMain : Form
    {
        const int BUFF_SIZE = 16 * 1024;
        const int PIC_W = 80;
        const int PIC_H = 60;

        byte[] buff_ = new byte[BUFF_SIZE];
        bool zoom2x = false;

        enum PicType
        {
            CamRaw, Backg, Gray, Xor, Filtered, NoiseReduced, Blobs
        }

        public frmMain()
        {
            InitializeComponent();
        }

        private void btnConnect_Click(object sender, EventArgs e)
        {
            if (serial_.IsOpen)
            {
                serial_.Close();
                lblStatus.Text = "Disconnected";
                btnConnect.Text = "Connect";
            }
            else
            {
                serial_.PortName = txtPort.Text;
                try
                {
                    serial_.Open();
                    lblStatus.Text = "Connected to " + txtPort.Text;
                    btnConnect.Text = "Disconnect";
                }
                catch (Exception ex)
                {
                    lblStatus.Text = "Could not connect to " + txtPort.Text;
                }
            }//else
        }

        private void ReadFilter()
        {
            //wait for data
            while (serial_.BytesToRead < 6)
                System.Threading.Thread.Sleep(20);

            serial_.Read(buff_, 0, 6);

            txtFilter.Text = (int)buff_[0] + ", " + (int)buff_[1] + ", " + (int)buff_[2] + " - ";
            txtFilter.Text += (int)buff_[3] + ", " + (int)buff_[4] + ", " + (int)buff_[5];
        }

        private void ReadPic(PicType type)
        {
            int size = PIC_H * PIC_W;

            if (type == PicType.CamRaw)
                size *= 2;

            //wait for data
            while (serial_.BytesToRead < size)
                System.Threading.Thread.Sleep(20);

            serial_.Read(buff_, 0, size);

            int inc = 1;
            int picW = PIC_W, picH = PIC_H;

            if (zoom2x)
            {
                inc++;
                picW *= 2;
                picH *= 2;
            }

            Bitmap bmp = new Bitmap(picW, picH);
            Graphics gr = Graphics.FromImage(bmp);
            gr.Clear(Color.White);

            int c = 0;
            if (type == PicType.CamRaw)
            {
                pic1.Image = bmp;

                for (int i = 0; i < picH; i += inc)
                    //for (int j = PIC_W; j >= 1; --j)
                    for (int j = 0; j < picW; j += inc)
                    {
                        int pix = buff_[c++] << 8;    //high byte
                        pix += buff_[c++];            //low byte

                        byte b = (byte)(pix & 0x1F);  //lower 5 bits
                        pix = pix >> 5;               //discard lower 5 bits
                        byte g = (byte)(pix & 0x3F);  //lower 6 bits;
                        g >>= 1;
                        pix = pix >> 6;               //discard lower 6 bits
                        byte r = (byte)(pix & 0x1F);  //rest 5 bits;
                        
                        Color col = Color.FromArgb(255 * r / 32, 255 * g / 32, 255 * b / 32);
                        bmp.SetPixel(j, i, col);

                        if (zoom2x)
                        {
                            bmp.SetPixel(j, i + 1, col);
                            bmp.SetPixel(j + 1, i, col);
                            bmp.SetPixel(j + 1, i + 1, col);
                        }
                    }//for
                return;
            }//if

            byte blob_inc = 50;
            byte last_pix = 0;

            for (int i = 0; i < picH; i += inc)
                //for (int j = PIC_W; j >= 1; --j)
                for (int j = 0; j < picW; j += inc)
                {
                    byte pix = buff_[c++];

                    if (pix != 0)
                    {
                        if (type == PicType.Xor || type == PicType.Filtered || type == PicType.NoiseReduced)
                                pix = 255;
                        else if (type == PicType.Blobs)
                        {
                            if (pix != last_pix)
                            {
                                pix += blob_inc;
                                last_pix = pix;
                                blob_inc += 50;
                            }
                        }//else if
                    }//if pix

                    Color col = Color.FromArgb(pix, pix, pix);
                    bmp.SetPixel(j, i, col);

                    if (zoom2x)
                    {
                        bmp.SetPixel(j, i + 1, col);
                        bmp.SetPixel(j + 1, i, col);
                        bmp.SetPixel(j + 1, i + 1, col);
                    }
                }//for j

            if (type == PicType.Backg)
                pic2.Image = bmp;
            else if (type == PicType.Gray)
                pic3.Image = bmp;
            else if (type == PicType.Xor)
                pic4.Image = bmp;
            else if (type == PicType.NoiseReduced)
                pic6.Image = bmp;

        }

        private void serial_DataReceived(object sender, System.IO.Ports.SerialDataReceivedEventArgs e)
        {
            const int CMD_SIZE = 3;
            while (true)
            {
                if (serial_.BytesToRead < CMD_SIZE)
                    return;

                serial_.Read(buff_, 0, CMD_SIZE);
                string data = Encoding.Default.GetString(buff_, 0, CMD_SIZE);

                string time = string.Format("{0:HH:mm:ss}", DateTime.Now);
                lblStatus.Invoke((MethodInvoker)delegate { lblTime.Text = time; });

                switch (data)
                {
                    case "SYN":
                        lblStatus.Invoke((MethodInvoker)delegate { lblStatus.Text = "Sync Done"; });
                        break;

                    case "RAW":
                        lblStatus.Invoke((MethodInvoker)delegate { lblStatus.Text = "Raw"; });
                        break;

                    case "CAM":
                        lblStatus.Invoke((MethodInvoker)delegate { lblStatus.Text = "Cam"; ReadPic(PicType.CamRaw); });
                        break;

                    case "BAK":
                        lblStatus.Invoke((MethodInvoker)delegate { lblStatus.Text = "Background"; ReadPic(PicType.Backg); });
                        break;

                    case "GRY":
                        lblStatus.Invoke((MethodInvoker)delegate { lblStatus.Text = "Gray"; ReadPic(PicType.Gray); });
                        break;

                    case "XOR":
                        lblStatus.Invoke((MethodInvoker)delegate { lblStatus.Text = "Xor"; ReadPic(PicType.Xor); });
                        break;

                    case "FIL":
                        lblStatus.Invoke((MethodInvoker)delegate { lblStatus.Text = "Filtered"; ReadPic(PicType.Filtered); });
                        break;

                    case "NOI":
                        lblStatus.Invoke((MethodInvoker)delegate { lblStatus.Text = "Filtered"; ReadPic(PicType.NoiseReduced); });
                        break;

                    case "BLO":
                        lblStatus.Invoke((MethodInvoker)delegate { lblStatus.Text = "Blobs"; ReadPic(PicType.Blobs); });
                        break;

                    case "LRN":
                        lblStatus.Invoke((MethodInvoker)delegate { lblStatus.Text = "Learned"; ReadFilter(); });
                        break;
                    
                    default:
                        lblStatus.Invoke((MethodInvoker)delegate { lblStatus.Text = "Error";});
                        serial_.DiscardInBuffer();
                        break;

                }//switch
            }//while
        }

        private void chkZoom_CheckedChanged(object sender, EventArgs e)
        {
            zoom2x = chkZoom.Checked;
        }

        private void btnCmdBackg_Click(object sender, EventArgs e)
        {
            lblStatus.Invoke((MethodInvoker)delegate { serial_.Write("TRK"); });
        }

        private void btnLearn_Click(object sender, EventArgs e)
        {
            lblStatus.Invoke((MethodInvoker)delegate { serial_.Write("LRN"); });
        }
    }
}

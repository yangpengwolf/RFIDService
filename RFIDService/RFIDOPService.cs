using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Linq;
using System.Collections;
using System.ServiceProcess;
using System.Text;
using System.Threading.Tasks;
using log4net;
using WebSocket4Net;
using ReaderB;
using System.IO.Ports;
using System.IO;
using System.Configuration;
[assembly: log4net.Config.XmlConfigurator(Watch = true)]
namespace RFIDService
{
    public partial class RFIDOPService : ServiceBase
    {

        private bool fAppClosed; //在测试模式下响应关闭应用程序
        private byte fComAdr = 0xff; //当前操作的ComAdr
        private int ferrorcode;
        private byte fBaud;
        private double fdminfre;
        private double fdmaxfre;
        private byte Maskadr;
        private byte MaskLen;
        private byte MaskFlag;
        private int fCmdRet = 30; //所有执行指令的返回值
        private int fOpenComIndex; //打开的串口索引号
        private bool fIsInventoryScan;
       
  
        private byte[] fOperEPC = new byte[36];
        private byte[] fPassWord = new byte[4];
        private byte[] fOperID_6B = new byte[8];
       ArrayList list = new ArrayList();
       
        private string fInventory_EPC_List; //存贮询查列表（如果读取的数据没有变化，则不进行刷新）
        private int frmcomportindex;
        private bool ComOpen = false;
        private bool breakflag = false;
        private string TIDno;

        protected string m_CurrentMessage = string.Empty;
        //private string comEName = "Xian Rabo Electronic TechnilogyCo.,Ltd.";
       // private string comCname = "西安瑞宝电子科技有限公司";
        private string comCnameHex = "897F5B89745E5B9D75355B5079D1628067099650516C53F8";
        System.Timers.Timer timer_inv = new System.Timers.Timer();
        System.Timers.Timer Timer_G2_Read = new System.Timers.Timer();
        WebSocket4Net.WebSocket websocket;
        ILog HelperLog = log4net.LogManager.GetLogger(typeof(Program));
        private string wsconstr = "ws://127.0.0.1:8080/channel/websocket";
        private string PWD = "1234567887654321";
        private int TypeFlag = 0;
       
        public RFIDOPService()
        {
            InitializeComponent();
            timer_inv.Interval = 2000;
            timer_inv.Enabled = false;
            timer_inv.Elapsed += new System.Timers.ElapsedEventHandler(timer_inv_Elapsed);
            wsconstr=ConfigurationSettings.AppSettings["WSConStr"].ToString();
            websocket = new WebSocket(wsconstr);           
            websocket.Opened += websocket_Opened;
            websocket.Closed += websocket_Closed;
            websocket.MessageReceived += websocket_MessageReceived;         

        }
           
        private string GetReturnCodeDesc(int cmdRet)
        {
            switch (cmdRet)
            {
                case 0x00:
                    return "操作成功";
                case 0x01:
                    return "询查时间结束前返回";
                case 0x02:
                    return "指定的询查时间溢出";
                case 0x03:
                    return "本条消息之后，还有消息";
                case 0x04:
                    return "读写模块存储空间已满";
                case 0x05:
                    return "访问密码错误";
                case 0x09:
                    return "销毁密码错误";
                case 0x0a:
                    return "销毁密码不能为全0";
                case 0x0b:
                    return "电子标签不支持该命令";
                case 0x0c:
                    return "对该命令，访问密码不能为全0";
                case 0x0d:
                    return "电子标签已经被设置了读保护，不能再次设置";
                case 0x0e:
                    return "电子标签没有被设置读保护，不需要解锁";
                case 0x10:
                    return "有字节空间被锁定，写入失败";
                case 0x11:
                    return "不能锁定";
                case 0x12:
                    return "已经锁定，不能再次锁定";
                case 0x13:
                    return "参数保存失败,但设置的值在读写模块断电前有效";
                case 0x14:
                    return "无法调整";
                case 0x15:
                    return "询查时间结束前返回";
                case 0x16:
                    return "指定的询查时间溢出";
                case 0x17:
                    return "本条消息之后，还有消息";
                case 0x18:
                    return "读写模块存储空间已满";
                case 0x19:
                    return "电子不支持该命令或者访问密码不能为0";
                case 0xFA:
                    return "有电子标签，但通信不畅，无法操作";
                case 0xFB:
                    return "无电子标签可操作";
                case 0xFC:
                    return "电子标签返回错误代码";
                case 0xFD:
                    return "命令长度错误";
                case 0xFE:
                    return "不合法的命令";
                case 0xFF:
                    return "参数错误";
                case 0x30:
                    return "通讯错误";
                case 0x31:
                    return "CRC校验错误";
                case 0x32:
                    return "返回数据长度有错误";
                case 0x33:
                    return "通讯繁忙，设备正在执行其他指令";
                case 0x34:
                    return "繁忙，指令正在执行";
                case 0x35:
                    return "端口已打开";
                case 0x36:
                    return "端口已关闭";
                case 0x37:
                    return "无效句柄";
                case 0x38:
                    return "无效端口";
                case 0xEE:
                    return "返回指令错误";
                default:
                    return "";
            }
        }
        private string GetErrorCodeDesc(int cmdRet)
        {
            switch (cmdRet)
            {
                case 0x00:
                    return "其它错误";
                case 0x03:
                    return "存储器超限或不被支持的PC值";
                case 0x04:
                    return "存储器锁定";
                case 0x0b:
                    return "电源不足";
                case 0x0f:
                    return "非特定错误";
                default:
                    return "";
            }
        }
        private byte[] HexStringToByteArray(string s)
        {
            s = s.Replace(" ", "");
            byte[] buffer = new byte[s.Length / 2];
            for (int i = 0; i < s.Length; i += 2)
                buffer[i / 2] = (byte)Convert.ToByte(s.Substring(i, 2), 16);
            return buffer;
        }
        private string ByteArrayToHexString(byte[] data)
        {
            StringBuilder sb = new StringBuilder(data.Length * 3);
            foreach (byte b in data)
                sb.Append(Convert.ToString(b, 16).PadLeft(2, '0'));
            return sb.ToString().ToUpper();

        }
           
        private void OpenPort()
        {
            int port = 0;
            int openresult, i;
            openresult = 30;
            string temp;
            //  Cursor = Cursors.WaitCursor;
         
            fComAdr = 0xFF; // $FF;
            try
            {
                if (true)//Auto
                {
                    fBaud = Convert.ToByte(3);
                    if (fBaud > 2)
                        fBaud = Convert.ToByte(fBaud + 2);
                    openresult = StaticClassReaderB.AutoOpenComPort(ref port, ref fComAdr, fBaud, ref frmcomportindex);
                    fOpenComIndex = frmcomportindex;
                    if (openresult == 0)
                    {
                        ComOpen = true;
                        // Button3_Click(sender, e); //自动执行读取写卡器信息
                        if (fBaud > 3)
                        {
                            //ComboBox_baud.SelectedIndex = Convert.ToInt32(fBaud - 2);
                        }
                        else
                        {
                            //ComboBox_baud.SelectedIndex = Convert.ToInt32(fBaud);
                        }
                        GetReaderInformation(); //自动执行读取写卡器信息
                        if ((fCmdRet == 0x35) | (fCmdRet == 0x30))
                        {
                            HelperLog.Info("串口通讯错误");
                            StaticClassReaderB.CloseSpecComPort(frmcomportindex);
                            ComOpen = false;
                        }
                    }
                }
                else
                {
                    //temp = ComboBox_COM.SelectedItem.ToString();
                    temp = temp.Trim();
                    port = Convert.ToInt32(temp.Substring(3, temp.Length - 3));
                    for (i = 6; i >= 0; i--)
                    {
                        fBaud = Convert.ToByte(i);
                        if (fBaud == 3)
                            continue;
                        openresult = StaticClassReaderB.OpenComPort(port, ref fComAdr, fBaud, ref frmcomportindex);
                        fOpenComIndex = frmcomportindex;
                        if (openresult == 0x35)
                        {
                            HelperLog.Info("串口已打开");
                            return;
                        }
                        if (openresult == 0)
                        {
                            ComOpen = true;
                            GetReaderInformation(); //自动执行读取写卡器信息
                            if (fBaud > 3)
                            {
                                //ComboBox_baud.SelectedIndex = Convert.ToInt32(fBaud - 2);
                            }
                            else
                            {
                                //ComboBox_baud.SelectedIndex = Convert.ToInt32(fBaud);
                            }
                            if ((fCmdRet == 0x35) || (fCmdRet == 0x30))
                            {
                                ComOpen = false;
                                HelperLog.Info("串口通讯错误");
                                StaticClassReaderB.CloseSpecComPort(frmcomportindex);
                                return;
                            }

                            break;
                        }

                    }
                }
            }
            catch (Exception ex)
            {
                HelperLog.Info("串口通讯异常"+ex.ToString());

            }
            finally
            {
                HelperLog.Info("串口通讯 Init");
            }

            if ((fOpenComIndex != -1) & (openresult != 0X35) & (openresult != 0X30))
            {
                fOpenComIndex = frmcomportindex;
              
                ComOpen = true;
            }
            if ((fOpenComIndex == -1) && (openresult == 0x30))
               HelperLog.Info("串口通讯错误");

           
        }
        private void GetReaderInformation()
        {
            byte[] TrType = new byte[2];
            byte[] VersionInfo = new byte[2];
            byte ReaderType = 0;
            byte ScanTime = 0;
            byte dmaxfre = 0;
            byte dminfre = 0;
            byte powerdBm = 0;
            byte FreBand = 0;

            fCmdRet = StaticClassReaderB.GetReaderInformation(ref fComAdr, VersionInfo, ref ReaderType, TrType, ref dmaxfre, ref dminfre, ref powerdBm, ref ScanTime, frmcomportindex);
            if (fCmdRet == 0)
            {
                
                FreBand = Convert.ToByte(((dmaxfre & 0xc0) >> 4) | (dminfre >> 6));
                switch (FreBand)
                {
                    case 0:
                        {
                          
                            fdminfre = 902.6 + (dminfre & 0x3F) * 0.4;
                            fdmaxfre = 902.6 + (dmaxfre & 0x3F) * 0.4;
                        }
                        break;
                    case 1:
                        {
                           
                            fdminfre = 920.125 + (dminfre & 0x3F) * 0.25;
                            fdmaxfre = 920.125 + (dmaxfre & 0x3F) * 0.25;
                        }
                        break;
                    case 2:
                        {
                           
                            fdminfre = 902.75 + (dminfre & 0x3F) * 0.5;
                            fdmaxfre = 902.75 + (dmaxfre & 0x3F) * 0.5;
                        }
                        break;
                    case 3:
                        {
                           
                            fdminfre = 917.1 + (dminfre & 0x3F) * 0.2;
                            fdmaxfre = 917.1 + (dmaxfre & 0x3F) * 0.2;
                        }
                        break;
                    case 4:
                        {
                           
                            fdminfre = 865.1 + (dminfre & 0x3F) * 0.2;
                            fdmaxfre = 865.1 + (dmaxfre & 0x3F) * 0.2;
                        }
                        break;
                }
                HelperLog.Info( Convert.ToString(fdminfre) + "MHz");
                HelperLog.Info( Convert.ToString(fdmaxfre) + "MHz");
                if (fdmaxfre != fdminfre)
                 
                if ((TrType[0] & 0x02) == 0x02) //第二个字节低第四位代表支持的协议“ISO/IEC 15693”
                {

                    HelperLog.Info("EPCC1G2 ISO180006B True");
                }
                else
                {
                    HelperLog.Info("EPCC1G2 ISO180006B Fasle");
                }
            }
            HelperLog.Info("GetReaderInformation"+ GetErrorCodeDesc(fCmdRet));
        }              
        private void ClosePort()
        {
            int port;

            port = fOpenComIndex;
                  
            fCmdRet = StaticClassReaderB.CloseSpecComPort(port);
                  
            if (fCmdRet == 0) ComOpen = false;
            HelperLog.Info("COM Closed!"); 
                
            
        }
        private void Inventory()
        {
            int MixFlag;
            int CardNum = 0;
            int Totallen = 0;
            int EPClen, m;
            byte[] EPC = new byte[5000];
            int CardIndex;
            string temps;
            string sEPC;             
            fIsInventoryScan = true;
            byte AdrTID = 0;
            byte LenTID = 0;
            byte TIDFlag = 0;            
                AdrTID = 0;
                LenTID = 0;
                TIDFlag = 0;
                HelperLog.Info("TypeFlag=" + TypeFlag);
             //TypeFlag = 1;
            fCmdRet = StaticClassReaderB.Inventory_G2(ref fComAdr, AdrTID, LenTID, TIDFlag, EPC, ref Totallen, ref CardNum, frmcomportindex);
            if ((fCmdRet == 1) | (fCmdRet == 2) | (fCmdRet == 3) | (fCmdRet == 4) | (fCmdRet == 0xFB))//代表已查找结束，
            {
                byte[] daw = new byte[Totallen];
                Array.Copy(EPC, daw, Totallen);
                temps = ByteArrayToHexString(daw);
                fInventory_EPC_List = temps;            //存贮记录
                m = 0;
                HelperLog.Info("CardNum =" + CardNum);
                if (CardNum == 0)
                {
                    fIsInventoryScan = false;
                    return;
                }
                if (TypeFlag < CardNum) 
                {
                    HelperLog.Info("More than other card！"); 
                    return;
                }               
                
                websocket.Send("%CardNum=" + CardNum);
                HelperLog.Info("-------------CardNum---------------- " + CardNum+"  Start");
                for (CardIndex = 0; CardIndex < CardNum; CardIndex++)
                {
                    EPClen = daw[m];
                    sEPC = temps.Substring(m * 2 + 2, EPClen * 2);
                    m = m + EPClen + 1;
                    if (sEPC.Length != EPClen * 2)
                        return;
                    TIDno = "";             
                    HelperLog.Info("EPCNo =" + sEPC);
                    HelperLog.Info("==============TID ==============");
                    G2_Read_Data(sEPC, "00000000", 0x02);
                    if (TypeFlag == 1)
                    {
                        string SWriteDate = DateTime.Now.ToString("yyyyMMdd");
                        HelperLog.Info("============WriteData ==========");
                        G2_DataWrite(sEPC, "87654321", 0x03, "10", comCnameHex);
                        G2_DataWrite(sEPC, "87654321", 0x03, "00", SWriteDate);
                        HelperLog.Info("============SetProtect ==========");
                        SetProtectState(sEPC, "87654321");
                        HelperLog.Info("============SetPWD ==========");
                        G2_DataWrite(sEPC, "87654321", 0x00,"00","1234567887654321"); 

                    }
                    websocket.Send("#ECP=&" + sEPC + "TID=|"+ TIDno);
                    if (fCmdRet == 0x00)
                    {
                        //websocket.Send("#ECP= " + sEPC + " TID= " + TIDno);
                        HelperLog.Info("ECP= " + sEPC + " TID= " + TIDno + " Init OK");
                    }
                    else
                    {
                        HelperLog.Info("ECP= " + sEPC + " TID= " + TIDno + " Init Fail" + fCmdRet);
                        websocket.Send("^ECP= " + sEPC + " Init Fail" + fCmdRet);
                    }
                   
                }
                HelperLog.Info("-------------CardNum---------------- " + CardNum + "  End");
            }
       
            fIsInventoryScan = false;
            if (fAppClosed)
            this.ClosePort();
        }
        
        private void InventoryNew()
        {
            int MixFlag;
            int CardNum = 0;
            int Totallen = 0;
            int EPClen, m;
            byte[] EPC = new byte[5000];
            int CardIndex;
            string temps;
            string sEPC, s;
            fIsInventoryScan = true;
            byte AdrTID = 0;
            byte LenTID = 0;
            byte TIDFlag = 0;
            AdrTID = 0;
            LenTID = 0;

            //ListViewItem aListItem = new ListViewItem();
            // HelperLog.Info("TypeFlag=" + TypeFlag);
            //TypeFlag = 1;
            fCmdRet = StaticClassReaderB.Inventory_G2(ref fComAdr, AdrTID, LenTID, TIDFlag, EPC, ref Totallen, ref CardNum, frmcomportindex);
            if ((fCmdRet == 1) | (fCmdRet == 2) | (fCmdRet == 3) | (fCmdRet == 4) | (fCmdRet == 0xFB))//代表已查找结束，
            {
                byte[] daw = new byte[Totallen];
                Array.Copy(EPC, daw, Totallen);
                temps = ByteArrayToHexString(daw);
                fInventory_EPC_List = temps;            //存贮记录
                m = 0;
                // HelperLog.Info("CardNum =" + CardNum);
                if (CardNum == 0)
                {
                    fIsInventoryScan = false;
                    return;
                }
                //if (TypeFlag < CardNum)
                //{
                //    HelperLog.Info("More than other card！");
                //    return;
                //}

                //websocket.Send("%CardNum=" + CardNum);
                //HelperLog.Info("-------------CardNum---------------- " + CardNum + "  Start");
                for (CardIndex = 0; CardIndex < CardNum; CardIndex++)
                {
                    EPClen = daw[m];
                    sEPC = temps.Substring(m * 2 + 2, EPClen * 2);
                    m = m + EPClen + 1;
                    if (sEPC.Length != EPClen * 2)
                        return;
                    TIDno = "";
                    //HelperLog.Info("EPCNo =" + sEPC);
                    //HelperLog.Info("==============TID ==============");
                    G2_Read_Data(sEPC, "00000000", 0x02);
                    if (TypeFlag == 1)
                    {
                        string SWriteDate = DateTime.Now.ToString("yyyyMMdd");
                        //HelperLog.Info("============WriteData ==========");
                        //G2_DataWrite(sEPC, "87654321", 0x03, "10", comCnameHex);
                        G2_DataWrite(sEPC, "87654321", 0x03, "00", SWriteDate);

                        HelperLog.Info("============SetProtect ==========");
                        SetProtectState(sEPC, "87654321");
                        // HelperLog.Info("============SetPWD ==========");
                        G2_DataWrite(sEPC, "87654321", 0x00, "00", "1234567887654321");
                         HelperLog.Info("============TIDToEPC ==========");
                        G2_DataWriteTIDtoEPC(sEPC, "87654321", 0x01, "02", TIDno);

                    }                   
                    if (fCmdRet == 0x00)
                    {
                        sEPC = TIDno;
                        websocket.Send("#ECP=&" + TIDno + "TID=|" + TIDno);
                       
                    }
                    else
                    {
                        HelperLog.Info("ECP= " + sEPC + " TID= " + TIDno + " Init Fail" + fCmdRet);
                        websocket.Send("^ECP= " + sEPC + " Init Fail" + fCmdRet);
                    }

                }
                HelperLog.Info("-------------CardNum---------------- " + CardNum + "  End");
            }

            fIsInventoryScan = false;
            if (fAppClosed)
                this.ClosePort();
                
        }
        private int G2_DataWriteTIDtoEPC(string EPCno, string PWD, byte iMEM, string startaddr, string sData)
        {
            byte WordPtr, ENum;
            byte Num = 0;
            byte Mem = 0;
            byte WNum = 0;
            byte EPClength = 0;
            byte Writedatalen = 0;
            int WrittenDataNum = 0;
            string s2, str;
            byte[] CardData = new byte[320];
            byte[] writedata = new byte[230];

            MaskFlag = 0;
            Maskadr = Convert.ToByte("00", 16);
            MaskLen = Convert.ToByte("00", 16);

            str = EPCno;
            //str = sData;
            ENum = Convert.ToByte(str.Length / 4);
            EPClength = Convert.ToByte(ENum * 2);
            byte[] EPC = new byte[ENum];
            EPC = HexStringToByteArray(str);
            //if (C_Reserve.Checked)
            //    Mem = 0;
            //if (C_EPC.Checked)
            //    Mem = 1;
            //if (C_TID.Checked)
            //    Mem = 2;
            //if (C_User.Checked)
            //    Mem = 3;
            //区域
            Mem = 1;

            if (PWD == "")
            {
                return -1;
            }
            //启始地址
            WordPtr = Convert.ToByte(startaddr, 16);
            Num = Convert.ToByte(6);
            if (PWD.Length != 8)
            {
                return -1;
            }
            fPassWord = HexStringToByteArray(PWD);
            if (sData == "")
                return -1;
            s2 = sData;
            if (s2.Length % 4 != 0)
            {
                //HelperLog.Info("以字为单位输入.");
                return -1;
            }
            WNum = Convert.ToByte(s2.Length / 4);
            byte[] Writedata = new byte[WNum * 2];
            Writedata = HexStringToByteArray(s2);
            Writedatalen = Convert.ToByte(WNum * 2);
            // add write TIDto EPC

            WordPtr = 1;
            int m, n;
            n = sData.Length;
           
            String pc = "";
            m = n / 4;
            m = (m & 0x3F) << 3;
            pc = Convert.ToString(m, 16).PadLeft(2, '0') + "00";
            Writedatalen = Convert.ToByte(sData.Length / 2 + 2);
            Writedata = HexStringToByteArray(pc + sData);          
            HelperLog.Debug("PC=" + pc);

            fCmdRet = StaticClassReaderB.WriteCard_G2(ref fComAdr, EPC, Mem, WordPtr, Writedatalen, Writedata, fPassWord, Maskadr, MaskLen, MaskFlag, WrittenDataNum, EPClength, ref ferrorcode, frmcomportindex);
            HelperLog.Debug("fCmdRet= " + fCmdRet + GetReturnCodeDesc(fCmdRet) + " ErrCode" + GetErrorCodeDesc(ferrorcode));
            if (fCmdRet == 0x05)
            {
                HelperLog.Debug("PWD 00000000");
                fPassWord = HexStringToByteArray("00000000");
                fCmdRet = StaticClassReaderB.WriteCard_G2(ref fComAdr, EPC, Mem, WordPtr, Writedatalen, Writedata, fPassWord, Maskadr, MaskLen, MaskFlag, WrittenDataNum, EPClength, ref ferrorcode, frmcomportindex);
                //HelperLog.Debug("fCmdRet= " + fCmdRet + GetReturnCodeDesc(fCmdRet));
            }
            if (fCmdRet == 0x0FA || fCmdRet == 0x0FB || fCmdRet == 0x0FC)
            {
                for (int i = 0; i < 5; i++)
                {
                    fCmdRet = StaticClassReaderB.WriteCard_G2(ref fComAdr, EPC, Mem, WordPtr, Writedatalen, Writedata, fPassWord, Maskadr, MaskLen, MaskFlag, WrittenDataNum, EPClength, ref ferrorcode, frmcomportindex);
                    //HelperLog.Debug("retry" + i + " fCmdRet= " + fCmdRet + GetReturnCodeDesc(fCmdRet) + " ErrCode" + GetErrorCodeDesc(ferrorcode));
                    if (fCmdRet == 0) break;
                }
            }

            if (fCmdRet == 0)
            {
                HelperLog.Info("Write TID To Down " + EPCno);
            }
            return fCmdRet;
        }


        protected  int G2_Read_Data(string EPCno,string PWD, byte iMEM )
        {
           
            fIsInventoryScan = true;
            byte WordPtr, ENum;
            byte Num = 0;
            byte Mem = 0;
            byte EPClength = 0;
            string str;
            byte[] CardData = new byte[320];

            MaskFlag = 0;
            Maskadr = Convert.ToByte("00", 16);
            MaskLen = Convert.ToByte("00", 16);

            // str = ComboBox_EPC2.SelectedItem.ToString();
            str = EPCno;
            ENum = Convert.ToByte(str.Length / 4);
            EPClength = Convert.ToByte(str.Length / 2);
            byte[] EPC = new byte[ENum * 2];
            EPC = HexStringToByteArray(str);
            //if (C_Reserve.Checked)
            //    Mem = 0;
            //if (C_EPC.Checked)
            //    Mem = 1;
            //if (C_TID.Checked)
            //    Mem = 2;
            //if (C_User.Checked)
            //    Mem = 3;
            Mem = iMEM;

            //Read TID
            WordPtr = Convert.ToByte("00", 16);
            Num = Convert.ToByte("6");

            fPassWord = HexStringToByteArray(PWD);
            fCmdRet = StaticClassReaderB.ReadCard_G2(ref fComAdr, EPC, Mem, WordPtr, Num, fPassWord, Maskadr, MaskLen, MaskFlag, CardData, EPClength, ref ferrorcode, frmcomportindex);
            //HelperLog.Info("fCmdRet="+fCmdRet); 
            if (fCmdRet == 0)
            {
                byte[] daw = new byte[Num * 2];
                Array.Copy(CardData, daw, Num * 2);
                TIDno = ByteArrayToHexString(daw);
                HelperLog.Info("TID =" + TIDno);
               // AddCmdLog("ReadData", "读", fCmdRet);
            }
            if (ferrorcode != -1)
            {
                HelperLog.Info(" '读' 返回错误=0x" + Convert.ToString(ferrorcode, 2) +
                 "(" + GetErrorCodeDesc(ferrorcode) + ")");
                ferrorcode = -1;
            }           
            fIsInventoryScan = false;
            if (fAppClosed)
                this.ClosePort();
            return fCmdRet;
        }

        private int G2_DataWrite(string EPCno, string PWD, byte iMEM,string startaddr,string sData)
        {
            byte WordPtr, ENum;
            byte Num = 0;
            byte Mem = 0;
            byte WNum = 0;
            byte EPClength = 0;
            byte Writedatalen = 0;
            int WrittenDataNum = 0;
            string s2, str;
            byte[] CardData = new byte[320];
            byte[] writedata = new byte[230];
            
            MaskFlag = 0;
            Maskadr = Convert.ToByte("00", 16);
            MaskLen = Convert.ToByte("00", 16);

            str = EPCno;
            ENum = Convert.ToByte(str.Length / 4);
            EPClength = Convert.ToByte(ENum * 2);
            byte[] EPC = new byte[ENum];
            EPC = HexStringToByteArray(str);
            //if (C_Reserve.Checked)
            //    Mem = 0;
            //if (C_EPC.Checked)
            //    Mem = 1;
            //if (C_TID.Checked)
            //    Mem = 2;
            //if (C_User.Checked)
            //    Mem = 3;
            //区域
            Mem = iMEM;
            
            if (PWD == "")
            {
                return -1;
            }
            //启始地址
            WordPtr = Convert.ToByte(startaddr, 16);
            Num = Convert.ToByte(4);
            if (PWD.Length != 8)
            {
                return -1;
            }
            fPassWord = HexStringToByteArray(PWD);
            if (sData == "")
                return -1;
            s2 = sData;
            if (s2.Length % 4 != 0)
            {
               HelperLog.Info("以字为单位输入.");
                return-1;
            }
            WNum = Convert.ToByte(s2.Length / 4);
            byte[] Writedata = new byte[WNum * 2];
            Writedata = HexStringToByteArray(s2);
            Writedatalen = Convert.ToByte(WNum * 2);
           
            fCmdRet = StaticClassReaderB.WriteCard_G2(ref fComAdr, EPC, Mem, WordPtr, Writedatalen, Writedata, fPassWord, Maskadr, MaskLen, MaskFlag, WrittenDataNum, EPClength, ref ferrorcode, frmcomportindex);
            //HelperLog.Debug("fCmdRet= " + fCmdRet + GetReturnCodeDesc(fCmdRet) + " ErrCode" + GetErrorCodeDesc(ferrorcode));
            if (fCmdRet == 0x05)             
            {
                HelperLog.Debug("PWD 00000000");
                fPassWord = HexStringToByteArray("00000000");
                fCmdRet = StaticClassReaderB.WriteCard_G2(ref fComAdr, EPC, Mem, WordPtr, Writedatalen, Writedata, fPassWord, Maskadr, MaskLen, MaskFlag, WrittenDataNum, EPClength, ref ferrorcode, frmcomportindex);
                HelperLog.Debug("fCmdRet= " + fCmdRet + GetReturnCodeDesc(fCmdRet));
            }
            if (fCmdRet == 0x0FA || fCmdRet == 0x0FB || fCmdRet == 0x0FC) 
            {
                for (int i = 0; i < 5; i++)
                {
                    fCmdRet = StaticClassReaderB.WriteCard_G2(ref fComAdr, EPC, Mem, WordPtr, Writedatalen, Writedata, fPassWord, Maskadr, MaskLen, MaskFlag, WrittenDataNum, EPClength, ref ferrorcode, frmcomportindex);
                    //HelperLog.Debug("retry"+i+" fCmdRet= " + fCmdRet + GetReturnCodeDesc(fCmdRet) +" ErrCode"+ GetErrorCodeDesc(ferrorcode));
                    if (fCmdRet == 0) break; 
                }
            } 
            
            if (fCmdRet == 0)
            {
               HelperLog.Info ( "Write Data Down "+EPCno);
            }
            return fCmdRet;
        }

        private int SetProtectState(string EPCno,string PWD )
        {
            byte select = 0;
            byte setprotect = 0;
            byte EPClength;
            string str;
            byte ENum;            
            MaskFlag = 0;
            Maskadr = Convert.ToByte("00", 16);
            MaskLen = Convert.ToByte("00", 16);

            str = EPCno;
            if (str == "")
                return -1;
            ENum = Convert.ToByte(str.Length / 4);
            EPClength = Convert.ToByte(str.Length / 2);
            byte[] EPC = new byte[ENum];
            EPC = HexStringToByteArray(str);
            if (PWD.Length != 8)
            {
               HelperLog.Info("访问密码小于8，重新输入");
                return -1;
            }
            fPassWord = HexStringToByteArray(PWD);

           // //if ((P_Reserve.Checked) & (DestroyCode.Checked))
           //     select = 0x00;
           //// else if ((P_Reserve.Checked) & (AccessCode.Checked))
           //     select = 0x01;
           //// else if (P_EPC.Checked)
           //     select = 0x02;
           //// else if (P_TID.Checked)
           //     select = 0x03;
           //// else if (P_User.Checked)
           //     select = 0x04;
           //     select = 0x00;

            // 有密码下读写
               setprotect = 0x02;
                for (byte i = 0; i < 0x05; i++)
                {
                    select = i;
                    if (select == 0x03) continue;
                    fCmdRet = StaticClassReaderB.SetCardProtect_G2(ref fComAdr, EPC, select, setprotect, fPassWord, Maskadr, MaskLen, MaskFlag, EPClength, ref ferrorcode, frmcomportindex); ;
                    if (fCmdRet == 0x05)
                    {
                        fPassWord = HexStringToByteArray("00000000");
                        fCmdRet = StaticClassReaderB.SetCardProtect_G2(ref fComAdr, EPC, select, setprotect, fPassWord, Maskadr, MaskLen, MaskFlag, EPClength, ref ferrorcode, frmcomportindex); ;
                                      
                    }
                    //fCmdRet += fCmdRet;
                    HelperLog.Debug("fCmdRet= " + fCmdRet + GetReturnCodeDesc(fCmdRet) + GetErrorCodeDesc(ferrorcode));
                }
                return fCmdRet;
                   
        }

        private void BlockWrite(string EPCno, string WriteData)
        {
            byte WordPtr, ENum;
            byte Num = 0;
            byte Mem = 0;
            byte WNum = 0;
            byte EPClength = 0;
            byte Writedatalen = 0;
            int WrittenDataNum = 0;
            string s2, str;
            byte[] CardData = new byte[320];
            byte[] writedata = new byte[230];
            //if ((maskadr_textbox.Text == "") || (maskLen_textBox.Text == ""))
            //{
            //    fIsInventoryScan = false;
            //    return;
            //}
            //if (checkBox1.Checked)
            //    MaskFlag = 1;
            //else
               MaskFlag = 0;
            Maskadr = Convert.ToByte("00", 16);
            MaskLen = Convert.ToByte("00", 16);
            //if (ComboBox_EPC2.Items.Count == 0)
            //    return;
            //if (ComboBox_EPC2.SelectedItem == null)
            //    return;
            str = EPCno;            
            if (str == "")
                return;
            ENum = Convert.ToByte(str.Length / 4);
            EPClength = Convert.ToByte(ENum * 2);
            byte[] EPC = new byte[ENum];
            EPC = HexStringToByteArray(str);
            //if (C_Reserve.Checked)
            //    Mem = 0;
            //if (C_EPC.Checked)
            //    Mem = 1;
            //if (C_TID.Checked)
            //    Mem = 2;
            //if (C_User.Checked)
            //    Mem = 3;
            //if (Edit_WordPtr.Text == "")
            //{
            //    HelperLog.Info("起始地址为空");
            //    return;
            //}
            //if (textBox1.Text == "")
            //{
            //    HelperLog.Info("读/块擦除长度");
            //    return;
            //}
            //if (Convert.ToInt32(Edit_WordPtr.Text, 16) + Convert.ToInt32(textBox1.Text) > 120)
            //    return;
            //if (Edit_AccessCode2.Text == "")
            //{
            //    return;
            //}
            //WordPtr = Convert.ToByte(Edit_WordPtr.Text, 16);
            //Num = Convert.ToByte(textBox1.Text);
            //if (Edit_AccessCode2.Text.Length != 8)
            //{
            //    return;
            //}
            //fPassWord = HexStringToByteArray(Edit_AccessCode2.Text);
            //if (Edit_WriteData.Text == "")
            //    return;
            s2 = WriteData;
            if (s2.Length % 4 != 0)
            {
               HelperLog.Info ("以字为单位输入.块写");
                return;
            }
            WNum = Convert.ToByte(s2.Length / 4);
            byte[] Writedata = new byte[WNum * 2];
            Writedata = HexStringToByteArray(s2);
            Writedatalen = Convert.ToByte(WNum * 2);
            //if ((checkBox_pc.Checked) && (C_EPC.Checked))
            //{
            //    WordPtr = 1;
            //    Writedatalen = Convert.ToByte(4 / 2 + 2);
            //    Writedata = HexStringToByteArray(textBox_pc.Text + Edit_WriteData.Text);
            //}
            WordPtr = 0;
            Writedatalen = Convert.ToByte(4 / 2 + 2);
            fCmdRet = StaticClassReaderB.WriteBlock_G2(ref fComAdr, EPC, Mem, WordPtr, Writedatalen, Writedata, fPassWord, Maskadr, MaskLen, MaskFlag, WrittenDataNum, EPClength, ref ferrorcode, frmcomportindex);
            
            if (fCmdRet == 0)
            {
                HelperLog.Info(" 返回=0x00块写成功");
            }
        }

        /// 作用：将字符串内容转化为16进制数据编码，其逆过程是Decode
        private string Encode(string strEncode)
        {
            string strReturn = "";//  存储转换后的编码
            foreach (short shortx in strEncode.ToCharArray())
            {
                strReturn += shortx.ToString("X4");
            }
            return strReturn;
        }

        /// 作用：将16进制数据编码转化为字符串，是Encode的逆过程

        private string Decode(string strDecode)
        {
            string sResult = "";
            for (int i = 0; i < strDecode.Length / 4; i++)
            {
                sResult += (char)short.Parse(strDecode.Substring(i * 4, 4), global::System.Globalization.NumberStyles.HexNumber);
            }
            return sResult;
        }

        protected override void OnStart(string[] args)
        {
         
            websocket.Open();
            HelperLog.Info("Start");
             OpenPort();
            HelperLog.Info("OPen");           
            //this.timer_inv.Enabled = true;         
                     
        }

        protected override void OnStop()
        {
            ClosePort();
            this.timer_inv.Enabled = false;        
            websocket.Close();        
            HelperLog.Info("RFIDService Stop");
            //websocket.Close(); 
        }

        protected override void OnPause()
        {
            this.timer_inv.Enabled = false;

        }

        protected override void OnContinue()
        {

            this.timer_inv.Enabled = true;
        }

        protected void websocket_Opened(object sender, EventArgs e)
        {
           
            websocket.Send("RFIDService Connect OK");

        }
        protected void websocket_Error(object sender, SuperSocket.ClientEngine.ErrorEventArgs e)
        {

            m_CurrentMessage = e.Exception.Message;

            if (e.Exception.InnerException != null)
            {
                m_CurrentMessage = e.Exception.InnerException.GetType().ToString();
            }
            HelperLog.Info(m_CurrentMessage);



        }
        protected void websocket_Closed(object sender, EventArgs e)
        {
            if (websocket.State.Equals(WebSocketState.Closing))
               websocket.Close();
        }
        protected void websocket_MessageReceived(object sender, MessageReceivedEventArgs e)
        {
            m_CurrentMessage = e.Message;
            int index = m_CurrentMessage.IndexOf("@");
            int indexpwd = m_CurrentMessage.IndexOf("$");
           // HelperLog.Info("Ser index+" + index);
            if (index > -1) 
            {
                //"@CMDE03 S01"
               string cmd = m_CurrentMessage.Substring(index + 1, 4);
               string a= m_CurrentMessage.Substring(index+5,2) ;
               //HelperLog.Info("Ser a+" + a);    
                if (a.Equals("01"))
                   TypeFlag = 1;
               else TypeFlag = 3;
                if (cmd.Equals("CMDS"))                  
                    this.timer_inv.Enabled = true;
                else if (cmd.Equals("CMDE"))
                    this.timer_inv.Enabled = false;



            }
            if (indexpwd > -1) 
            {
                string spwd = m_CurrentMessage.Substring(indexpwd);
                HelperLog.Info("PassWord" + spwd);
            }
            HelperLog.Info("Ser MSG+"+m_CurrentMessage);
           
        }

        private void timer_inv_Elapsed(object sender, System.Timers.ElapsedEventArgs e)
        {
            this.timer_inv.Enabled = false;           
             Inventory();
            //InventoryNew();
            this.timer_inv.Enabled = true;
        }
    
    }
}

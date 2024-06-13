using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TDH3Flash
{
    public static class Flash
    {
        
        public static void Go(string[] args)
        {
            Out("TD-H3 Firmware Flasher\r\n");
            switch (args.Length)
            {
                case 0:
                    Out("Available serial ports:");
                    foreach(var name in SerialPort.GetPortNames())
                    {
                        Out(name);
                    }
                    Usage();
                    break;
                case 1:
                    Out("Not enough parameters");
                    Usage();
                    break;
                default:
                    Begin(args[0], string.Join(' ', args, 1, args.Length - 1));
                    break;

            }
        }

        public static void Begin(string port, string file)
        {
            byte[] bin;
            try { bin = File.ReadAllBytes(file); } catch { Out($"Cannot read file {file}"); return; }
            int len = (int)Math.Ceiling(bin.Length / 32.0) * 32;
            byte[] firmware = new byte[len];
            Array.Copy(bin, 0, firmware, 0, bin.Length);
            if (len < 40000 || len > 65536) { Out($"{file} is not the correct size to be a valid firmware file"); return; }
            SerialPort sp;
            try
            {
                sp = new(port, 115200, Parity.None, 8, StopBits.One)
                {
                    ReadTimeout = 500
                };
                sp.Open();
            }
            catch { Out($"Unable to open serial port {port}"); return; }
            Out("Turn off radio, hold PTT and turn radio on keeping PTT held.");
            bool found = false;
            while(true)
            {
                int b;
                try { b = sp.ReadByte(); } 
                catch(TimeoutException) { if (found) break; else continue; }
                catch { b = -1; }
                if (b == -1)
                { 
                    Out("Serial read error (HS)");
                    return;
                }
                if (b == 0xa5)
                {
                    if (!found)
                    {
                        found = true;
                        Out("Radio Detected");
                        byte[] init = [ 0xA0, 0xEE, 0x74, 0x71, 0x07, 0x74, 0x55, 0x55,
                                        0x55, 0x55 ,0x55 ,0x55 ,0x55 ,0x55 ,0x55 ,0x55,
                                        0x55, 0x55 ,0x55 ,0x55 ,0x55 ,0x55 ,0x55 ,0x55,
                                        0x55, 0x55 ,0x55 ,0x55 ,0x55 ,0x55 ,0x55 ,0x55,
                                        0x55, 0x55 ,0x55 ,0x55];
                        try { sp.Write(init, 0, init.Length); } catch { Out("Serial send error (HS)"); return; }
                    }
                }
                else
                {
                    if(found)
                    {
                        Out("Serial read unexpected data (HS)");
                        return;
                    }
                }
            }
            for (int i = 0, blk = 0; i < len; i += 32, blk++)
            {
                if ((i % 0x800) == 0)
                    Out($"> Flashing {i:X4}");
                byte[] pkt = new byte[36];
                Array.Copy(firmware, i, pkt, 4, 32);
                for (int j = 4; j < 36; j++) pkt[3] += pkt[j];
                pkt[0] = 0xa1;
                if (i + 32 >= len) pkt[0]++;
                pkt[1] = (byte)((blk >> 8) & 0xff);
                pkt[2] = (byte)(blk & 0xff);
                try { sp.Write(pkt, 0, pkt.Length); } catch { Out($"Serial send error (BLK:{blk})"); return; }
                try
                {
                    if (sp.ReadByte() != 0xA3) { Out($"Ack error (BLK:{blk})"); return; }
                }
                catch (TimeoutException) { Out($"Timeout error (BLK:{blk})"); return; }
                catch { Out($"Serial read error (BLK:{blk})"); return; }
            }
            Out("Completed.");
        }

        public static void Usage()
        {
            Out("\r\nUsage:");
            Out("TDH3Flash <port> <firmware bin file>");
        }

        public static void Out(string message)
        {
            Console.WriteLine(message);
        }
    }
}

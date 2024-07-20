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
        private static string? hexFile = null;
        public static void Go(string[] args)
        {
            Out("TD-H3/8 Firmware Flasher\r\n");
            switch (args.Length)
            {
                case 0:
                    Out("Available serial ports:");
                    foreach(var name in SerialPort.GetPortNames())
                        Out(name);
                    Usage();
                    break;
                case 1:
                    Out("Not enough parameters");
                    Usage();
                    break;
                default:
                    if (args[0].ToLower().EndsWith(".hex") && hexFile == null)
                    {
                        hexFile = args[0];
                        Go(args[1..]);
                    }
                    else
                        Begin(args[0], string.Join(' ', args, 1, args.Length - 1).Trim());
                    break;

            }
        }

        public static void Begin(string port, string file)
        {
            byte[] bin;
            try { bin = File.ReadAllBytes(file); } catch { Out($"Cannot read file {file}"); return; }
            Out($"Firmware length: {bin.Length}");
            byte[] firmware;
            int newLength;
            if (hexFile != null)
            {
                string hex;
                try { hex = File.ReadAllText(hexFile); } catch { Out($"Cannot read hex file {hexFile}"); return; }
                newLength = ApplyIHex(bin, hex, out string err, false);
                if (newLength == 0) { Out($"Cannot process hex file: {err}"); return; }
                newLength = (int)Math.Ceiling(newLength / 32.0) * 32;
                if (newLength > 0xf800) { Out($"Patched firmware is too large. Size:{newLength} Maximum:{0xf800}"); return; }
                firmware = new byte[newLength];
                Array.Copy(bin, 0, firmware, 0, bin.Length);
                ApplyIHex(firmware, hex, out _, true);
                Out($"Hex patch applied, new length: {newLength}");
                try
                {
                    string patchedFile = $"{file}.patched";
                    File.WriteAllBytes(patchedFile, firmware);
                    Out($"Written patched binary to: {patchedFile}");
                }
                catch { }
            }
            else
            {
                newLength = (int)Math.Ceiling(bin.Length / 32.0) * 32;
                Out($"Firmware padded length: {newLength}");
                firmware = new byte[newLength];
                Array.Copy(bin, 0, firmware, 0, bin.Length);
            }
            if (newLength < 30000 || newLength > 0xf800) { Out($"{file} is not the correct size to be a valid firmware file"); return; }
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
            Out("Turn off radio, hold PTT (H3) or FlashLight (H8) and turn radio on keeping the button held.");
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
            for (int i = 0, blk = 0; i < newLength; i += 32, blk++)
            {
                if ((i % 0x800) == 0)
                    Out($"> Flashing {i:X4}");
                byte[] pkt = new byte[36];
                Array.Copy(firmware, i, pkt, 4, 32);
                for (int j = 4; j < 36; j++) pkt[3] += pkt[j];
                pkt[0] = 0xa1;
                if (i + 32 >= newLength) pkt[0]++;
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

        public static int ApplyIHex(byte[] data, string hex, out string error, bool forReal)
        {
            int fwLength = data.Length;
            foreach (string hexLine in hex.Split('\n'))
            {
                string ihex = hexLine.Replace('\t', ' ').Replace('\r', ' ').Trim();
                try
                {
                    if (ihex.StartsWith(':'))
                    {
                        int cnt = Convert.ToInt32(ihex.Substring(1, 2), 16);
                        int addh = Convert.ToInt32(ihex.Substring(3, 2), 16);
                        int addl = Convert.ToInt32(ihex.Substring(5, 2), 16);
                        int add = (addh << 8) | addl;
                        int type = Convert.ToInt32(ihex.Substring(7, 2), 16);
                        if (type != 0) continue;
                        int cs = cnt + addh + addl + type;
                        int i = 0;
                        for (; i < cnt; i++)
                        {
                            string s = ihex.Substring(9 + (2 * i), 2);
                            int b = Convert.ToInt32(s, 16);
                            cs += b;
                            if (forReal)
                                data[add] = (byte)b;
                            if (add > fwLength - 1) fwLength = add + 1;
                            add++;
                        }
                        int cs1 = Convert.ToInt32(ihex.Substring(9 + (2 * i), 2), 16);
                        cs &= 0xff;
                        cs = (0x100 - cs) & 0xff;
                        if (cs != cs1) { error = "Bad checksum"; return 0; }
                    }
                }
                catch { error = "Invalid iHex"; return 0; }
            }
            error = string.Empty;
            return fwLength;
        }
        public static void Usage()
        {
            Out("\r\nUsage:");
            Out("TDH3Flash [Intel hex patchfile] <port> <firmware bin file>");
        }

        public static void Out(string message)
        {
            Console.WriteLine(message);
        }
    }
}

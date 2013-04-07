using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using Microsoft.SPOT;
using Microsoft.SPOT.Hardware;
using SecretLabs.NETMF.Hardware;
using SecretLabs.NETMF.Hardware.NetduinoPlus;
using IRDecoder;

// Program to receive IR codes from an IR receiver module
// Decodes two Panasonic air conditioning infrared protocols
// * E12-CKP (remote control A75C2295)
// * E12-DKE (remote control A75C2616)
//
// Connect the IR receiver module data pin to pin D7
//
// Tested with IR receiver module A742724 from Panasonic E12-CKP indoor unit
//
// The code is based heavily on this example from 'phil': 
// http://forums.netduino.com/index.php?/topic/185-rc6-decoder-class/

namespace IRdemo
{
    public class Program
    {
        public static void Main()
        {
            // Create an instance of the IR decoder
            // listens on pin D7 and calls IRCodeReceived with the decoded message
            IRDecoder.IRDecoder IR = new IRDecoder.IRDecoder(Pins.GPIO_PIN_D7, IRCodeReceived);

            // Main thread just sleeps, everything is handled in events
            Thread.Sleep(Timeout.Infinite);
        }

        // Event handler for the code received event
        static void IRCodeReceived(String protocol, String marks, byte[] bytes)
        {
            String bytestring = "";

            Debug.Print("Received code: " + protocol + "\n" + marks);

            // Decode hex bytes of known protocols
            if (protocol.Substring(0, "Unknown".Length) != "Unknown")
            {
                for (int i = 0; i < bytes.Length; i++)
                {
                    bytestring += bytes[i].ToString("x2");

                    if (i + 1 < bytes.Length)
                    {
                        bytestring += "-";
                    }
                }

                Debug.Print("bytes decoded:\n" + bytestring);

                // Panasonic DKE checksum
                // 0xF4 plus all bytes but the last == last byte
                if (protocol == "Panasonic DKE")
                {
                    byte checksum = 0xF4;
                    for (int i = 0; i < (bytes.Length - 1); i++)
                    {
                        checksum += bytes[i];
                    }

                    if (checksum == bytes[bytes.Length - 1])
                    {
                        Debug.Print("Panasonic DKE checksum OK");
                    }
                    else
                    {
                        Debug.Print("Panasonic DKE checksum FAILS");
                    }
                }
                else if (protocol == "Panasonic CKP")
                {
                    PrintPanasonicCKP(bytes);
                }
            }
        }

        private static void PrintPanasonicCKP(byte[] bytes)
        {
            // Short message is about setting
            // * ION
            // * QUIET
            // * POWERFUL
            // * SWINGS

            if ((bytes.Length == 12) &&
                CheckRepeated(bytes, 0, 4, 3) &&
                CheckRepeatedPairs(bytes))
            {
                if ((bytes[0] == 0x48) && (bytes[2] == 0x33))
                {
                    Debug.Print("ION ON/OFF");
                }
                else if ((bytes[0] == 0x81) && (bytes[2] == 0x33))
                {
                    Debug.Print("QUIET ON/OFF");
                }
                else if ((bytes[0] == 0x86) && (bytes[2] == 0x35))
                {
                    Debug.Print("POWERFUL ON/OFF");
                }
                else if ((bytes[0] == 0x80) && (bytes[2] == 0x30))
                {
                    Debug.Print("VSWING AUTO");
                }
                else if (((bytes[0] & 0XF0) == 0xA0) && (bytes[2] == 0x32))
                {
                    switch (bytes[0] & 0X0F)
                    {
                        case 0x01:
                            Debug.Print("VSWING FULL UP");
                            break;
                        case 0x02:
                            Debug.Print("VSWING MIDDLE UP");
                            break;
                        case 0x03:
                            Debug.Print("VSWING MIDDLE");
                            break;
                        case 0x04:
                            Debug.Print("VSWING MIDDLE DOWN");
                            break;
                        case 0x05:
                            Debug.Print("VSWING DOWN");
                            break;
                        case 0x08:
                            Debug.Print("HSWING AUTO");
                            break;
                        case 0x09:
                            Debug.Print("HSWING MIDDLE");
                            break;
                        case 0x0C:
                            Debug.Print("HSWING LEFT");
                            break;
                        case 0x0D:
                            Debug.Print("HSWING MIDDLE LEFT");
                            break;
                        case 0x0E:
                            Debug.Print("HSWING MIDDLE RIGHT");
                            break;
                        case 0x0F:
                            Debug.Print("HSWING RIGHT");
                            break;
                        default:
                            Debug.Print("Unknown SWING");
                            break;
                    }
                }
            }

            // Everything else is a long message
            // Timer messages not yet covered
            else if ((bytes.Length == 16) &&
                        CheckRepeated(bytes, 0, 4, 2) &&
                        CheckRepeated(bytes, 8, 4, 2) &&
                        CheckRepeatedPairs(bytes) &&
                        (bytes[10] == 0x36))
            {
                String Mode = "Unknown";
                String Fan = "AUTO";
                String SwingV;
                String SwingH;

                // Operation mode, low bits of byte 3

                switch (bytes[2] & 0x07)
                {
                    case 0x06:
                        Mode = "AUTO";
                        break;
                    case 0x04:
                        Mode = "HEAT";
                        break;
                    case 0x02:
                        Mode = "COOL";
                        break;
                    case 0x03:
                        Mode = "DRY";
                        break;
                    case 0x01:
                        Mode = "FAN";
                        break;
                }

                if ((bytes[2] & 0x08) != 0x08)
                {
                    Mode += " ON/OFF";
                }

                // Fan speed, high bits of byte 1

                int fan = bytes[0] & 0xF0;
                if (fan != 0xF0)
                {
                    Fan = ((fan >> 4) - 1).ToString();
                }

                // Vertical swing, high bits of byte 9

                switch (bytes[8] & 0XF0)
                {
                    case 0xF0:
                        SwingV = "AUTO";
                        break;
                    case 0xD0:
                        SwingV = "FULL DOWN";
                        break;
                    case 0xC0:
                        SwingV = "MIDDLE DOWN";
                        break;
                    case 0xB0:
                        SwingV = "MIDDLE";
                        break;
                    case 0xA0:
                        SwingV = "MIDDLE UP";
                        break;
                    case 0x90:
                        SwingV = "FULL UP";
                        break;
                    default:
                        SwingV = "Unknown";
                        break;
                }

                // Horizontal swing, low bits of byte 9

                switch (bytes[8] & 0x0F)
                {
                    case 0x08:
                        SwingH = "AUTO";
                        break;
                    case 0x00:
                        SwingH = "MANUAL";
                        break;
                    default:
                        SwingH = "Unknown";
                        break;
                }

                // Print the state

                Debug.Print("MODE   " + Mode);
                Debug.Print("FAN    " + Fan);
                if (Mode != "FAN")
                {
                    Debug.Print("TEMP   " + ((int)(bytes[0] & 0x0F) + 15));
                }
                Debug.Print("SWINGV " + SwingV);
                Debug.Print("SWINGH " + SwingH);
            }
            else
            {
                Debug.Print("protocol error");
            }
        }

        // Check for repeated patterns in the byte array
        static Boolean CheckRepeated(byte[] byteArray, int startingPosition, int offset, int repeats)
        {
            for (int i = 0; i < repeats; i++)
            {
                for (int j = startingPosition; j < offset; j++)
                {
                    if (byteArray[j] != byteArray[j + i * offset])
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        // Check for repeated pairs in the byte array
        static Boolean CheckRepeatedPairs(byte[] byteArray)
        {
            for (int i = 0; i < byteArray.Length; i += 2)
            {
                if (byteArray[i] != byteArray[i + 1])
                {
                    return false;
                }
            }

            return true;
        }
    }
}

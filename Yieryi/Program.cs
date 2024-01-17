using System;
using System.IO.Ports;
using System.Threading;

namespace Yieryi
{
    class Program
    {
        enum ResponseFormat
        {
            Ph,
            Orp
        }

        static ushort CalculateModbusCrc(byte[] data, int offset, int count)
        {
            ushort crc = 0xFFFF;

            for (int i = 0; i + offset < data.Length && i < count; i++)
            {
                crc ^= data[i + offset];  // XOR byte into least significant byte of crc

                for (int j = 8; j > 0; j--) // Loop over each bit
                {
                    if ((crc & 0x0001) != 0) // If the LSB is set
                    {
                        crc >>= 1; // Shift right and XOR 0xA001
                        crc ^= 0xA001;
                    }
                    else            // Else LSB is not set
                        crc >>= 1; // Just shift right
                }
            }
            // Note: This algorithm returns CRC in 'little endian' byte order
            return crc;
        }
        static void VerifyModbusCrc(byte[] data)
        {
            ushort crc = (ushort)((data[data.Length - 1] << 8) | data[data.Length - 2]);
            var calculatedCrc = CalculateModbusCrc(data, 0, data.Length - 2);
            if (crc != calculatedCrc)
            {
                throw new ArgumentOutOfRangeException($"CRC 0x{calculatedCrc:X4} does not match 0x{crc:X4}");
            }
        }
        static byte[] AppendModbusCrc(byte[] data)
        {
            byte[] crcData = new byte[data.Length + 2];
            Array.Copy(data, crcData, data.Length);
            ushort crc = CalculateModbusCrc(data, 0, data.Length);
            crcData[crcData.Length - 2] = (byte)(crc & 0xFF);
            crcData[crcData.Length - 1] = (byte)((crc >> 8) & 0xFF);
            return crcData;
        }
        static byte[] ReadBytes(SerialPort serialPort, int count)
        {
            byte[] data = new byte[count];
            int bytesRead = 0;
            while (bytesRead < count)
            {
                int readCount = serialPort.Read(data, bytesRead, count - bytesRead);
                if (readCount > 0)
                {
                    bytesRead += readCount;
                }
            }
            return data;
        }
        static byte[] IssueReadData(SerialPort serialPort, byte address)
        {
            byte[] readCommand = AppendModbusCrc(new byte[]{
                address, 0x03, 0x00, 0x00, 0x00, 0x04
            });
            VerifyModbusCrc(readCommand);

            serialPort.DiscardInBuffer();
            serialPort.Write(readCommand, 0, readCommand.Length);
            var response = ReadBytes(serialPort, 16);
            VerifyModbusCrc(response);

            return response;
        }
        static void IssueSetResponseFormat(SerialPort serialPort, byte address, ResponseFormat format)
        {
            byte[] setOrpFormatCommand = AppendModbusCrc(new byte[] {
                address, 0x06, 0x00, 0x05, 0x00, (byte)(format == ResponseFormat.Orp ? 0x01 : 0x00)
            });
            VerifyModbusCrc(setOrpFormatCommand);

            serialPort.DiscardInBuffer();
            serialPort.Write(setOrpFormatCommand, 0, setOrpFormatCommand.Length);
        }

        static void Main(string[] args)
        {
            string portName = args[0]; // Replace with your COM port
            int baudRate = 9600;
            Parity parity = Parity.None;
            StopBits stopBits = StopBits.One;
            byte address = 0x01;
            ResponseFormat responseFormat = ResponseFormat.Ph;
            bool switchBetweenResponseFormats = true;

            using (SerialPort serialPort = new SerialPort(portName, baudRate, parity, 8, stopBits))
            {
                try
                {
                    serialPort.ReadTimeout = 1000;
                    serialPort.Open();

                    // init orp format to known value
                    IssueSetResponseFormat(serialPort, address, responseFormat);
                    Thread.Sleep(1000); // longer delay otherwise orp <-> ph values sometimes are mixed

                    for (int t = 0; t < 10000; t++)
                    {
                        try
                        {
                            var response = IssueReadData(serialPort, address);

                            var cf = ((response[4] << 8) | response[5]) / 1000.0;
                            var ph = ((response[6] << 8) | response[7]) / 100.0;
                            // orp has strange format
                            var orp = ((response[6] & (byte)0x40) == 0 ? 1 : -1) * (((response[6] & 0x3F) << 8) | response[7]);
                            var re = ((response[8] << 8) | response[9]) / 100.0;
                            var temp = ((response[10] << 8) | response[11]) / 10.0;

                            Console.Write($"cf: {cf}, ");
                            if (responseFormat == ResponseFormat.Ph)
                            {
                                Console.Write($"ph: {ph}, ");
                            }
                            else
                            {
                                Console.Write($"orp: {orp}, ");
                            }
                            Console.WriteLine($"re: {re}, temp: {temp}");

                            Thread.Sleep(200); // wait 200ms before next request to allow device to recover.

                            // switch orp / ph format to read both values
                            // it seems that when switch format command is issued over RS485
                            // device does not store it since turning it on and off will restore previous format
                            // set with on device buttons. If this were not the case then such frequent eeprom changes
                            // could quickly damage device persistent storage since number of writes is usually limited
                            if (switchBetweenResponseFormats)
                            {
                                responseFormat = responseFormat == ResponseFormat.Orp ? ResponseFormat.Ph : ResponseFormat.Orp;
                                IssueSetResponseFormat(serialPort, address, responseFormat);
                                Thread.Sleep(1000); // longer delay otherwise orp <-> ph values sometimes are mixed
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Error: {ex.Message}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Error: " + ex.Message);
                }
                finally
                {
                    if (serialPort.IsOpen)
                    {
                        serialPort.Close();
                    }
                }
            }

            // Exit the program
            Console.WriteLine("Press any key to exit...");
            Console.ReadKey();
        }
    }

}

using System;
using System.IO.Ports;
using System.Threading;

namespace Yieryi
{
    class Program
    {
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

        static void Main(string[] args)
        {
            string portName = args[0]; // Replace with your COM port
            int baudRate = 9600;
            Parity parity = Parity.None;
            StopBits stopBits = StopBits.One;
            byte[] commandToSend = { 0x01, 0x03, 0x00, 0x00, 0x00, 0x04, 0x44, 0x09 };
            VerifyModbusCrc(commandToSend);

            using (SerialPort serialPort = new SerialPort(portName, baudRate, parity, 8, stopBits))
            {
                try
                {
                    serialPort.ReadTimeout = 1000;
                    serialPort.Open();

                    for (int t = 0; t < 10000; t++)
                    {
                        try
                        {

                            serialPort.Write(commandToSend, 0, commandToSend.Length);
                            var response = ReadBytes(serialPort, 16);
                            VerifyModbusCrc(response);

                            var cf = ((response[4] << 8) | response[5]) / 1000.0;
                            var ph = ((response[6] << 8) | response[7]) / 100.0;
                            var re = ((response[8] << 8) | response[9]) / 100.0;
                            var temp = ((response[10] << 8) | response[11]) / 10.0;

                            Console.WriteLine($"cf: {cf}, ph: {ph}, re: {re}, temp: {temp}");

                            Thread.Sleep(500); // wait 150ms before next request.

                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine("Error: " + ex.Message);
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

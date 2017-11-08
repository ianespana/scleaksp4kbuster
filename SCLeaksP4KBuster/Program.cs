using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ZstdNet;

namespace SCLeaksP4KBuster
{
    internal static class Program
    {
        public static void Main(string[] args)
        {
            using (FileStream fs = new FileStream(".\\Data.p4k", FileMode.Open, FileAccess.Read))
            {
                ReadP4K(fs, new FileInfo(".\\Data.p4k").Length);
            }
        }

        private static void ReadP4K(FileStream stream, long p4KSize)
        {
            long byteCount = 16;
            BinaryReader br = new BinaryReader(stream);

            do
            {
                byte[] buffer = br.ReadBytes(16);
                byteCount += 16;
                if (IsThisAFile(buffer))
                {
                    byte fileType = buffer[8];
                    if (fileType != 0x64 && fileType != 0x00)
                    {
                        Console.WriteLine("Ignoring broken file");
                    }
                    else
                    {


                        buffer = br.ReadBytes(16);
                        byteCount += 16;
                        int bytesToSkip = BitConverter.ToInt16(buffer, 12);
                        long byteToSkipFrom = byteCount;
//                        int bytesToSkip = buffer[13] << 8 | buffer[12];

                        int filenameLength = BitConverter.ToInt16(buffer, 10);
//                        int filenameLength = buffer[11] << 8 | buffer[10];
                        byte[] filenameBytes = new byte[filenameLength];
                        filenameBytes[0] = buffer[14];
                        filenameBytes[1] = buffer[15];
                        int filenameIndex = 2;
                        int lastCharIndex = 0;

                        while (filenameIndex < filenameLength)
                        {
                            buffer = br.ReadBytes(16);
                            byteCount += 16;

                            for (int i = 0; i < buffer.Length; i++)
                            {
                                if (filenameIndex >= filenameLength)
                                {
                                    lastCharIndex = i;
                                    break;
                                }

                                filenameBytes[filenameIndex] = buffer[i];
                                filenameIndex++;
                            }
                        }

                        string fileName = System.Text.Encoding.ASCII.GetString(filenameBytes);

                        byte[] compFileSizeBytes = new byte[4];
                        int compFileSizeCount = 0;
                        
                        int compOffset = lastCharIndex + 4;
                        if (compOffset >= buffer.Length)
                        {
                            compOffset -= 16;
                        }
                        
                        do
                        {
                            if (compFileSizeCount > 0)
                            {
                                buffer = br.ReadBytes(16);
                                byteCount += 16;
                                compOffset -= 16;
                            }
                            
                            int i = compFileSizeCount > 0 ? 0 : compOffset;
                            for (; i < buffer.Length; i++)
                            {
                                if (compFileSizeCount >= 4)
                                {
                                    break;
                                }
                                compFileSizeBytes[compFileSizeCount] = buffer[i];
                                compFileSizeCount++;
                            }
                        } while (compFileSizeCount < 4);
                        
                        int compFileSize = BitConverter.ToInt32(compFileSizeBytes, 0);
                        
                        byte[] fileSizeBytes = new byte[4];
                        int fileSizeCount = 0;
                        
                        int offset = compOffset + 8;
                        
                        while (byteCount % 16 != 0)
                        {
                            br.ReadByte();
                            byteCount++;
                            offset--;
                        }
                        
                        if (offset >= buffer.Length)
                        {
                            buffer = br.ReadBytes(16);
                            byteCount += 16;
                            offset -= 16;
                        }
                        
                        do
                        {
                            int i = fileSizeCount > 0 ? 0 : offset;
                            for (; i < buffer.Length; i++)
                            {
                                if (fileSizeCount >= 4)
                                {
                                    break;
                                }
                                fileSizeBytes[fileSizeCount] = buffer[i];
                                fileSizeCount++;
                            }

                            if (fileSizeCount < 4)
                            {
                                buffer = br.ReadBytes(16);
                                byteCount += 16;
                            }
                        } while (fileSizeCount < 4);

                        int fileSize = BitConverter.ToInt32(fileSizeBytes, 0);
//                        long fileSize = fileSizeBytes[3] << 32 | fileSizeBytes[2] << 16 | fileSizeBytes[1] << 8 | fileSizeBytes[0];
                        
//                        long cursorPosDifference = byteCount - byteToSkipFrom;
                        int bytesToJump = bytesToSkip - lastCharIndex - 32;
                        br.ReadBytes(bytesToJump);
                        byteCount += bytesToJump;
                        
                        while (byteCount % 16 != 0)
                        {
                            br.ReadByte();
                            byteCount++;
                        }
                        
                        buffer = br.ReadBytes(16);
                        if (buffer.SequenceEqual(new byte[]
                        {
                            0x00, 0x00, 0x00, 0x00,
                            0x00, 0x00, 0x00, 0x00,
                            0x00, 0x00, 0x00, 0x00,
                            0x00, 0x00, 0x00, 0x00
                        }))
                        {
                            buffer = br.ReadBytes(16);
                        }
                        
                        MemoryStream ms = new MemoryStream();
                        do
                        {
                            if (ms.Length + 16 >= fileSize)
                            {
                                int lastZero = 0;
                                for (int i = buffer.Length - 1; i >= 0; i--)
                                {
                                    if (buffer[i] == 0x00)
                                    {
                                        lastZero = i;
                                    }
                                    else
                                    {
                                        break;
                                    }
                                }
                                ms.Write(buffer, 0, lastZero);
                                break;
                            }
                            else
                            {
                                ms.Write(buffer, 0, 16);
                                buffer = br.ReadBytes(16);
                                byteCount += 16;
                            }
                        } while (ms.Length < fileSize);

                        CreateDirectoryForFile(fileName);
                        if (fileType == 0x64)
                        {
                            using (Decompressor decomp = new Decompressor())
                            {
                                byte[] compData = ms.ToArray();
//                                byte[] decompData = new byte[]; 
//                                int size = decomp.Unwrap(compData, decompData, 0);
                                byte[] decompData = new byte[compFileSize];
                                try
                                {
                                    int size = decomp.Unwrap(compData, decompData, 0);
                                }
                                catch (ZstdException)
                                {
                                    Console.WriteLine("Skipping the following file because it is broken: " + fileName);
                                }
                                using (FileStream fs = new FileStream(fileName, FileMode.Create, FileAccess.Write))
                                {
                                    fs.Write(decompData, 0, decompData.Length);
                                }
                            }
                        }
                        else
                        {
                            using (FileStream fs = new FileStream(fileName, FileMode.Create, FileAccess.Write))
                            {
                                ms.WriteTo(fs);
                            }
                        }

                        //
                        Console.WriteLine("File Name: " + fileName);
                        Console.WriteLine("File Size: " + ms.Length);
                        Console.WriteLine("Parsed: " + byteCount + " bytes");
                        Console.WriteLine("");

                        ms.Close();

                        while (byteCount % 16 != 0)
                        {
                            br.ReadByte();
                            byteCount++;
                        }
                        //
                    }
                }
                
                Console.WriteLine("Scanning P4K file. Current position: " + byteCount);
                ClearLine();
            } while (byteCount < p4KSize);
            
            br.Close();
        }
        
        private static void CreateDirectoryForFile(string path)
        {
            string directoryName = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directoryName) && !Directory.Exists(directoryName))
            {
                Directory.CreateDirectory(directoryName);
            }
        }

        private static bool IsThisAFile(byte[] bytes)
        {
            byte[] fileSignature = {0x50, 0x4B, 0x03, 0x14, 0x2D};
            
            if (bytes.Length < fileSignature.Length) return false;

            bool fileFound = true;
            for (int i = 0; i < fileSignature.Length; i++)
            {
                if (fileSignature[i] != bytes[i])
                {
                    fileFound = false;
                    break;
                }
            }

            return fileFound;
        }
        
        private static void ClearLine()
        {
            Console.SetCursorPosition(0, Console.CursorTop - 1);
            Console.Write(new string(' ', Console.WindowWidth));
            Console.SetCursorPosition(0, Console.CursorTop - 1);
        }
    }
}
﻿using System;
using System.Collections.Generic;
using System.IO;
using Libraries.ByteArray;
using PSOCT.Unitxt;

namespace PSOCT
{
    public abstract class UnitxtDC
    {
        // This includes the tables pointer, at 12
        public static int stringGroupCount = 37;

        public static void JsonToBin(string filename)
        {
            byte[] data = File.ReadAllBytes(filename);
            Dictionary<string, int> stringAddresses = new Dictionary<string, int>();
            UnitxtFile unitxt = Json.Deserialize<UnitxtFile>(data, 0, data.Length);

            int pr3_pointers = 0;
            // Add all the strings as well as the group pointer
            for (int i1 = 0; i1 < 44; i1++)
            {
                pr3_pointers += 1;
                pr3_pointers += unitxt.StringGroups[i1].entries.Count;
            }

            // Whatever, this should be enough, every time
            ByteArray baPr2 = new ByteArray(1024 * 1024);
            ByteArray baPr3 = new ByteArray((pr3_pointers + 5) * 2 + 32);

            for (int i1 = 0; i1 < 44; i1++)
            {
                for (int i2 = 0; i2 < unitxt.StringGroups[i1].entries.Count; i2++)
                {
                    // Only add this string if we don't have it yet, 
                    // Gotta save the bytes
                    if (!stringAddresses.ContainsKey(unitxt.StringGroups[i1].entries[i2]))
                    {
                        // Save it's address
                        stringAddresses[unitxt.StringGroups[i1].entries[i2]] = baPr2.Position;
                        // Write it out
                        baPr2.WriteStringA(unitxt.StringGroups[i1].entries[i2], 0, unitxt.StringGroups[i1].entries[i2].Length, true);
                        // Some padding?
                        baPr2.Pad(4);
                    }
                }
            }

            // Write the tables
            // We'll need the first one 
            List<int> tablePointers = new List<int>();
            for (int i1 = 0; i1 < unitxt.SomeTables.Count; i1++)
            {
                // Save the table offset
                tablePointers.Add(baPr2.Position);
                for (int i2 = 0; i2 < unitxt.SomeTables[i1].Count; i2++)
                {
                    baPr2.Write(unitxt.SomeTables[i1][i2]);
                }
            }

            // We'll need this offset, it's the beginning of the short table pointer in pr3
            int tablePointer = baPr2.Position;
            for (int i1 = 0; i1 < unitxt.SomeTables.Count; i1++)
            {
                // Save the table offset
                baPr2.Write(tablePointers[i1]);
            }
            // Table count offset, needed at the end
            int tableCountOffset = baPr2.Position;
            baPr2.Write(2);
            baPr2.Write(tablePointer);

            for (int i1 = 0; i1 < 44; i1++)
            {
                unitxt.StringGroups[i1].groupOffset = baPr2.Position;
                for (int i2 = 0; i2 < unitxt.StringGroups[i1].entries.Count; i2++)
                {
                    // Instead of getting the addresses from the strings themselves
                    // Just use the dict, no duplicates :)
                    baPr2.Write(stringAddresses[unitxt.StringGroups[i1].entries[i2]]);
                }
            }
            int stringGroupOffset = baPr2.Position;
            for (int i1 = 0; i1 < 44; i1++)
            {
                baPr2.Write(unitxt.StringGroups[i1].groupOffset);
            }
            int tableCountOffsetOffset = baPr2.Position;
            baPr2.Write(tableCountOffset);
            baPr2.Write(stringGroupOffset);

            baPr2.Resize(baPr2.Position);

            // Write Pr3 data
            baPr3.Write(0x20);
            baPr3.Write(pr3_pointers + 5);
            baPr3.Write(1);
            baPr3.Write(0);
            baPr3.Write(tableCountOffsetOffset);
            baPr3.Write(0);
            baPr3.Write(0);
            baPr3.Write(0);

            // Just fill this stuff
            baPr3.Write((short)(tablePointer / 4));
            baPr3.Write((short)1);
            baPr3.Write((short)2);
            for (int i1 = 0; i1 < pr3_pointers; i1++)
            {
                baPr3.Write((short)1);
            }
            baPr3.Write((short)1);
            baPr3.Write((short)1);

            uint prc_key = (uint)(DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalSeconds;
            byte[] dataPR2 = PSOCT.CompressPRC(baPr2.Buffer, prc_key, false);
            byte[] dataPR3 = PSOCT.CompressPRC(baPr3.Buffer, prc_key, false);

            File.WriteAllBytes(Path.ChangeExtension(filename, ".pr2"), dataPR2);
            File.WriteAllBytes(Path.ChangeExtension(filename, ".pr3"), dataPR3);
        }
        public static void BinToJson(string filename)
        {
            // Create this early on
            UnitxtFile unitxt = new UnitxtFile();

            string pathPR2 = Path.ChangeExtension(filename, "pr2");
            string pathPR3 = Path.ChangeExtension(filename, "pr3");

            byte[] dataPR2 = File.ReadAllBytes(pathPR2);
            byte[] dataPR3 = File.ReadAllBytes(pathPR3);

            dataPR2 = PSOCT.DecompressPRC(dataPR2, false);
            dataPR3 = PSOCT.DecompressPRC(dataPR3, false);

            ByteArray baPR2 = new ByteArray(dataPR2);
            ByteArray baPR3 = new ByteArray(dataPR3);

            int shortPointerTableOffset = baPR3.ReadI32();
            int shortPointerTableCount = baPR3.ReadI32();
            baPR3.Position = shortPointerTableOffset;

            List<int> shortPointerTable = new List<int>();
            int chain = 0;

            for (int i1 = 0; i1 < shortPointerTableCount; i1++)
            {
                chain = baPR3.ReadI16() * 4 + chain;
                shortPointerTable.Add(chain);
            }

            int unitxtTablesPointer = shortPointerTable[shortPointerTable.Count - stringGroupCount + 12];
            baPR2.Position = baPR2.ReadI32(unitxtTablesPointer);
            unitxt.tableValue = baPR2.ReadI32();

            unitxt.SomeTables2.Add(new List<byte>());
            int unitxtTablesPointer2 = baPR2.ReadI32();
            for (int i1 = 0; i1 < 0x70; i1++)
            {
                byte value = baPR2.ReadU8(unitxtTablesPointer2 + i1);
                unitxt.SomeTables2[0].Add(value);
            }

            int unitxtTablePointer = baPR2.ReadI32();
            for (int i1 = 0; i1 < 2; i1++)
            {
                int unitxtTableOffset = baPR2.ReadI32(unitxtTablePointer + i1 * 4);

                unitxt.SomeTables.Add(new List<short>());
                for (int i2 = 0; i2 < 0xE0; i2++)
                {
                    short value = baPR2.ReadI16(unitxtTableOffset + i2 * 2);
                    unitxt.SomeTables[i1].Add(value);
                }
            }

            unitxt.SomeTables2.Add(new List<byte>());
            unitxtTablesPointer2 = baPR2.ReadI32();
            for (int i1 = 0; i1 < 0x30; i1++)
            {
                byte value = baPR2.ReadU8(unitxtTablesPointer2 + i1);
                unitxt.SomeTables2[1].Add(value);
            }

            int stringGroupsCurrentIndex = 0;
            for (int i1 = 0; i1 < stringGroupCount; i1++)
            {
                if (i1 == 12)
                {
                    continue;
                }

                unitxt.StringGroups.Add(new UnitxtGroup() { name = string.Format("Group {0:D2}", i1) });

                int groupPointer = shortPointerTable[shortPointerTable.Count - stringGroupCount + i1];
                int groupAddress = baPR2.ReadI32(groupPointer);

                int nextGroupPointer = shortPointerTable[shortPointerTable.Count - stringGroupCount + i1];
                int nextGroupAddress = baPR2.ReadI32(nextGroupPointer);

                // This one goes into the table itself so gotta do some magic
                if (i1 == 9)
                {
                    nextGroupPointer = shortPointerTable[shortPointerTable.Count - stringGroupCount + 12];
                    nextGroupAddress = baPR2.ReadI32(unitxtTablesPointer);
                    nextGroupAddress += 8;
                    nextGroupAddress = baPR2.ReadI32(nextGroupAddress);
                    nextGroupAddress = baPR2.ReadI32(nextGroupAddress);
                }
                else if (i1 == 11)
                {
                    nextGroupPointer = shortPointerTable[shortPointerTable.Count - stringGroupCount + i1 + 2];
                    nextGroupAddress = baPR2.ReadI32(nextGroupPointer);
                }
                else if (i1 == (stringGroupCount - 1))
                {
                    nextGroupPointer = shortPointerTable[shortPointerTable.Count - stringGroupCount];
                    nextGroupAddress = shortPointerTable[shortPointerTable.Count - stringGroupCount];// baPR2.ReadI32(nextGroupPointer);
                }
                else
                {
                    nextGroupPointer = shortPointerTable[shortPointerTable.Count - stringGroupCount + i1 + 1];
                    nextGroupAddress = baPR2.ReadI32(nextGroupPointer);
                }

                while (groupAddress < nextGroupAddress)
                {
                    int stringPointer = baPR2.ReadI32(groupAddress);
                    string text = baPR2.ReadStringA(-1, stringPointer);
                    unitxt.StringGroups[stringGroupsCurrentIndex].entries.Add(text);

                    groupAddress += 4;
                }
                unitxt.StringGroups[stringGroupsCurrentIndex].count = unitxt.StringGroups[stringGroupsCurrentIndex].entries.Count;
                stringGroupsCurrentIndex++;
            }

            string jsonText = Json.Serialize(unitxt, true);
            File.WriteAllText(Path.ChangeExtension(filename, ".json"), jsonText);
        }
    }
}
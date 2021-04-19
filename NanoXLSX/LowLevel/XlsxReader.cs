﻿/*
 * NanoXLSX is a small .NET library to generate and read XLSX (Microsoft Excel 2007 or newer) files in an easy and native way  
 * Copyright Raphael Stoeckli © 2021
 * This library is licensed under the MIT License.
 * You find a copy of the license in project folder or on: http://opensource.org/licenses/MIT
 */

using NanoXLSX.Styles;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Threading.Tasks;
using IOException = NanoXLSX.Exceptions.IOException;

namespace NanoXLSX.LowLevel
{
    /// <summary>
    /// Class representing a reader to decompile XLSX files
    /// </summary>
    public class XlsxReader
    {

        #region privateFields
        private string filePath;
        private Stream inputStream;
        private Dictionary<int, WorksheetReader> worksheets;
        private MemoryStream memoryStream;
        private WorkbookReader workbook;
        private ImportOptions importOptions;
        private StyleReaderContainer styleReaderContainer;
        #endregion

        #region constructors
        /// <summary>
        /// Constructor with file path as parameter
        /// </summary>
        /// <param name="options">Import options to override the automatic approach of the reader. <see cref="ImportOptions"/> for information about import options.</param>
        /// <param name="path">File path of the XLSX file to load</param>
        public XlsxReader(String path, ImportOptions options = null)
        {
            filePath = path;
            importOptions = options;
            worksheets = new Dictionary<int, WorksheetReader>();
        }

        /// <summary>
        /// Constructor with stream as parameter
        /// </summary>
        /// <param name="options">Import options to override the automatic approach of the reader. <see cref="ImportOptions"/> for information about import options.</param>
        /// <param name="stream">Stream of the XLSX file to load</param>
        public XlsxReader(Stream stream, ImportOptions options = null)
        {
            importOptions = options;
            worksheets = new Dictionary<int, WorksheetReader>();
            inputStream = stream;
        }
        #endregion

        #region methods

        /// <summary>
        /// Reads the XLSX file from a file path or a file stream
        /// </summary>
        /// <exception cref="Exceptions.IOException">
        /// Throws IOException in case of an error
        /// </exception>
        public void Read()
        {
            try
            {
                using (memoryStream = new MemoryStream())
                {
                    ZipArchive zf;
                    if (inputStream == null && !string.IsNullOrEmpty(filePath))
                    {
                        using (FileStream fs = new FileStream(filePath, FileMode.Open))
                        {
                            fs.CopyTo(memoryStream);
                        }
                    }
                    else if (inputStream != null)
                    {
                        using (inputStream)
                        {
                            inputStream.CopyTo(memoryStream);
                        }
                    }
                    else
                    {
                        throw new IOException("LoadException", "No valid stream or file path was provided to open");
                    }

                    memoryStream.Position = 0;
                    zf = new ZipArchive(memoryStream, ZipArchiveMode.Read);
                    MemoryStream ms;

                    SharedStringsReader sharedStrings = new SharedStringsReader();
                    ms = GetEntryStream("xl/sharedStrings.xml", zf);
                    if (ms.Length > 0) // If length == 0, no shared strings are defined (no text in file)
                    {
                        sharedStrings.Read(ms);
                    }

                    StyleReader styleReader = new StyleReader();
                    ms = GetEntryStream("xl/styles.xml", zf);
                    styleReader.Read(ms);
                    styleReaderContainer = styleReader.StyleReaderContainer;

                    workbook = new WorkbookReader();
                    ms = GetEntryStream("xl/workbook.xml", zf);
                    workbook.Read(ms);

                    int worksheetIndex = 1;
                    string name;
                    string nameTemplate;
                    WorksheetReader wr;
                    nameTemplate = "sheet" + worksheetIndex.ToString(CultureInfo.InvariantCulture) + ".xml";
                    name = "xl/worksheets/" + nameTemplate;
                    foreach (KeyValuePair<int, string> definition in workbook.WorksheetDefinitions)
                    {
                        ms = GetEntryStream(name, zf);
                        wr = new WorksheetReader(sharedStrings, nameTemplate, worksheetIndex, styleReaderContainer, importOptions);
                        wr.Read(ms);
                        worksheets.Add(definition.Key, wr);
                        worksheetIndex++;
                        nameTemplate = "sheet" + worksheetIndex.ToString(CultureInfo.InvariantCulture) + ".xml";
                        name = "xl/worksheets/" + nameTemplate;
                    }
                }
            }
            catch (Exception ex)
            {
                throw new IOException("LoadException", "There was an error while reading an XLSX file. Please see the inner exception:", ex);
            }
        }

        /// <summary>
        /// Reads the XLSX file from a file path or a file stream asynchronous
        /// </summary>
        /// <exception cref="Exceptions.IOException">
        /// May throw an IOException in case of an error. The asynchronous operation may hide the exception.
        /// </exception>
        /// <returns>Task object (void)</returns>
        public async Task ReadAsync()
        {
            await Task.Run(() =>
            {
                Read();
            }).ConfigureAwait(false);
        }

        /// <summary>
        /// Resolves the workbook with all worksheets from the loaded file
        /// </summary>
        /// <returns>Workbook object</returns>
        public Workbook GetWorkbook()
        {
            Workbook wb = new Workbook(false);
            Worksheet ws;
            int index = 1;
            foreach (KeyValuePair<int, WorksheetReader> reader in worksheets)
            {
                ws = new Worksheet(workbook.WorksheetDefinitions[reader.Key], index, wb);
                foreach (KeyValuePair<string, Cell> cell in reader.Value.Data)
                {
                    cell.Value.WorksheetReference = ws;
                    if (reader.Value.StyleAssignment.ContainsKey(cell.Key))
                    {
                        Style style = styleReaderContainer.GetStyle(reader.Value.StyleAssignment[cell.Key], true);
                        if (style != null)
                        {
                            cell.Value.SetStyle(style);
                        }
                    }
                    ws.AddCell(cell.Value, cell.Key);
                }
                wb.AddWorksheet(ws);
                index++;
            }
            return wb;
        }

        /// <summary>
        /// Gets the memory stream of the specified file in the archive (XLSX file)
        /// </summary>
        /// <param name="name">Name of the XML file within the XLSX file</param>
        /// <param name="archive">Zip file (XLSX)</param>
        /// <returns>MemoryStream object of the specified file</returns>
        private MemoryStream GetEntryStream(string name, ZipArchive archive)
        {
            for (int i = 0; i < archive.Entries.Count; i++)
            {
                if (archive.Entries[i].FullName == name)
                {
                    MemoryStream ms = new MemoryStream();
                    archive.Entries[i].Open().CopyTo(ms);
                    ms.Position = 0;
                    return ms;
                }
            }
            return new MemoryStream();
        }

        #endregion

    }
}

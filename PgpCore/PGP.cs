﻿using Org.BouncyCastle.Asn1.Pkcs;
using Org.BouncyCastle.Asn1.X509;
using Org.BouncyCastle.Bcpg;
using Org.BouncyCastle.Bcpg.OpenPgp;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Math;
using Org.BouncyCastle.Pkcs;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.X509;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace PgpCore
{
    public enum PGPFileType { Binary, Text, UTF8 }

    public class PGP : IDisposable
    {
        public static readonly PGP Instance = new PGP();

        private const int    BufferSize      = 0x10000;
        private const string DefaultFileName = "name";

        public CompressionAlgorithmTag CompressionAlgorithm
        {
            get;
            set;
        }

        public SymmetricKeyAlgorithmTag SymmetricKeyAlgorithm
        {
            get;
            set;
        }

        public int PgpSignatureType
        {
            get;
            set;
        }

        public PublicKeyAlgorithmTag PublicKeyAlgorithm
        {
            get;
            set;
        }
        public PGPFileType FileType
        {
            get;
            set;
        }

        public HashAlgorithmTag HashAlgorithmTag
        {
            get;
            set;
        }

        #region Constructor

        public PGP()
        {
            CompressionAlgorithm = CompressionAlgorithmTag.Uncompressed;
            SymmetricKeyAlgorithm = SymmetricKeyAlgorithmTag.TripleDes;
            PgpSignatureType = PgpSignature.DefaultCertification;
            PublicKeyAlgorithm = PublicKeyAlgorithmTag.RsaGeneral;
            FileType = PGPFileType.Binary;
            HashAlgorithmTag = HashAlgorithmTag.Sha1;
        }

        #endregion Constructor

        #region Encrypt

        public void EncryptFile(string inputFilePath, string outputFilePath, string publicKeyFilePath,
            bool armor = true, bool withIntegrityCheck = true)
        {
            EncryptFileAsync(inputFilePath, outputFilePath, publicKeyFilePath, armor, withIntegrityCheck).Wait();
        }

        public void EncryptFile(string inputFilePath, string outputFilePath, IEnumerable<string> publicKeyFilePaths,
            bool armor = true, bool withIntegrityCheck = true)
        {
            EncryptFileAsync(inputFilePath, outputFilePath, publicKeyFilePaths, armor, withIntegrityCheck).Wait();
        }

        public void EncryptStream(Stream inputStream, Stream outputStream, Stream publicKeyStream,
            bool armor = true, bool withIntegrityCheck = true)
        {
            EncryptStreamAsync(inputStream, outputStream, publicKeyStream, armor, withIntegrityCheck).Wait();
        }

        public void EncryptStream(Stream inputStream, Stream outputStream, IEnumerable<Stream> publicKeyStreams,
            bool armor = true, bool withIntegrityCheck = true)
        {
            EncryptStreamAsync(inputStream, outputStream, publicKeyStreams, armor, withIntegrityCheck).Wait();
        }

        /// <summary>
        /// PGP Encrypt the file.
        /// </summary>
        /// <param name="inputFilePath"></param>
        /// <param name="outputFilePath"></param>
        /// <param name="publicKeyFilePath"></param>
        /// <param name="armor"></param>
        /// <param name="withIntegrityCheck"></param>
        public async Task EncryptFileAsync(
            string inputFilePath,
            string outputFilePath,
            string publicKeyFilePath,
            bool armor = true,
            bool withIntegrityCheck = true)
        {
            await EncryptFileAsync(inputFilePath, outputFilePath, new[] { publicKeyFilePath }, armor, withIntegrityCheck);
        }

        /// <summary>
        /// PGP Encrypt the file.
        /// </summary>
        /// <param name="inputFilePath"></param>
        /// <param name="outputFilePath"></param>
        /// <param name="publicKeyFilePaths"></param>
        /// <param name="armor"></param>
        /// <param name="withIntegrityCheck"></param>
        public async Task EncryptFileAsync(
            string inputFilePath,
            string outputFilePath,
            IEnumerable<string> publicKeyFilePaths,
            bool armor = true,
            bool withIntegrityCheck = true)
        {
            //Avoid multiple enumerations of 'publicKeyFilePaths'
            string[] publicKeys = publicKeyFilePaths.ToArray();

            if (String.IsNullOrEmpty(inputFilePath))
                throw new ArgumentException("InputFilePath");
            if (String.IsNullOrEmpty(outputFilePath))
                throw new ArgumentException("OutputFilePath");
            if (!File.Exists(inputFilePath))
                throw new FileNotFoundException(String.Format("Input file [{0}] does not exist.", inputFilePath));
            foreach (string publicKeyFilePath in publicKeys)
            {
                if (String.IsNullOrEmpty(publicKeyFilePath))
                    throw new ArgumentException(nameof(publicKeyFilePath));
                if (!File.Exists(publicKeyFilePath))
                    throw new FileNotFoundException(String.Format("Input file [{0}] does not exist.", publicKeyFilePath));
            }

            List<Stream> publicKeyStreams = new List<Stream>();

            foreach (string publicKeyFilePath in publicKeyFilePaths)
            {
                MemoryStream memoryStream = new MemoryStream();
                using (Stream publicKeyStream = new FileStream(publicKeyFilePath, FileMode.Open))
                {
                    await publicKeyStream.CopyToAsync(memoryStream);
                    memoryStream.Position = 0;
                    publicKeyStreams.Add(memoryStream);
                }
            }

            using (FileStream inputStream = new FileStream(inputFilePath, FileMode.Open))
            using (Stream outputStream = File.Create(outputFilePath))
                await EncryptStreamAsync(inputStream, outputStream, publicKeyStreams, armor, withIntegrityCheck);
        }

        /// <summary>
        /// PGP Encrypt the stream.
        /// </summary>
        /// <param name="inputStream"></param>
        /// <param name="outputStream"></param>
        /// <param name="publicKeyStream"></param>
        /// <param name="armor"></param>
        /// <param name="withIntegrityCheck"></param>
        public async Task EncryptStreamAsync(
            Stream inputStream,
            Stream outputStream,
            Stream publicKeyStream,
            bool armor = true,
            bool withIntegrityCheck = true)
        {
            await EncryptStreamAsync(inputStream, outputStream, new[] { publicKeyStream }, armor, withIntegrityCheck);
        }

        /// <summary>
        /// PGP Encrypt the stream.
        /// </summary>
        /// <param name="inputStream"></param>
        /// <param name="outputStream"></param>
        /// <param name="publicKeyStreams"></param>
        /// <param name="armor"></param>
        /// <param name="withIntegrityCheck"></param>
        public async Task EncryptStreamAsync(Stream inputStream, Stream outputStream, IEnumerable<Stream> publicKeyStreams, bool armor = true, bool withIntegrityCheck = true)
        {
            //Avoid multiple enumerations of 'publicKeyFilePaths'
            Stream[] publicKeys = publicKeyStreams.ToArray();

            if (inputStream == null)
                throw new ArgumentException("InputStream");
            if (outputStream == null)
                throw new ArgumentException("OutputStream");
            foreach (Stream publicKey in publicKeys)
            {
                if (publicKey == null)
                    throw new ArgumentException("PublicKeyStream");
            }
            
            string fileName = DefaultFileName;

            if (inputStream is FileStream)
            {
                string inputFilePath = ((FileStream)inputStream).Name;
                fileName = Path.GetFileName(inputFilePath);
            }

            if (armor)
            {
                outputStream = new ArmoredOutputStream(outputStream);
            }

            PgpEncryptedDataGenerator pk = new PgpEncryptedDataGenerator(SymmetricKeyAlgorithm, withIntegrityCheck, new SecureRandom());

            foreach (Stream publicKey in publicKeys)
            {
                pk.AddMethod(Utilities.ReadPublicKey(publicKey));
            }

            Stream @out = pk.Open(outputStream, new byte[1 << 16]);

            if (CompressionAlgorithm != CompressionAlgorithmTag.Uncompressed)
            {
                PgpCompressedDataGenerator comData = new PgpCompressedDataGenerator(CompressionAlgorithm);
                await Utilities.WriteStreamToLiteralDataAsync(comData.Open(@out), FileTypeToChar(), inputStream, fileName);
                comData.Close();
            }
            else
                await Utilities.WriteStreamToLiteralDataAsync(@out, FileTypeToChar(), inputStream, fileName);

            @out.Close();

            if (armor)
            {
                outputStream.Close();
            }
        }

        #endregion Encrypt

        #region Encrypt and Sign

        public void EncryptFileAndSign(string inputFilePath, string outputFilePath, string publicKeyFilePath, string privateKeyFilePath,
            string passPhrase, bool armor = true, bool withIntegrityCheck = true)
        {
            EncryptFileAndSignAsync(inputFilePath, outputFilePath, publicKeyFilePath, privateKeyFilePath, passPhrase, armor, withIntegrityCheck).Wait();
        }

        public void EncryptFileAndSign(string inputFilePath, string outputFilePath, IEnumerable<string> publicKeyFilePaths, string privateKeyFilePath,
            string passPhrase, bool armor = true, bool withIntegrityCheck = true)
        {
            EncryptFileAndSignAsync(inputFilePath, outputFilePath, publicKeyFilePaths, privateKeyFilePath, passPhrase, armor, withIntegrityCheck).Wait();
        }

        public void EncryptStreamAndSign(Stream inputStream, Stream outputStream, Stream publicKeyStream, Stream privateKeyStream, string passPhrase, bool armor = true, bool withIntegrityCheck = true)
        {
            EncryptStreamAndSignAsync(inputStream, outputStream, publicKeyStream, privateKeyStream, passPhrase, armor, withIntegrityCheck).Wait();
        }

        public void EncryptStreamAndSign(Stream inputStream, Stream outputStream, IEnumerable<Stream> publicKeyStreams, Stream privateKeyStream, string passPhrase, bool armor = true, bool withIntegrityCheck = true)
        {
            EncryptStreamAndSignAsync(inputStream, outputStream, publicKeyStreams, privateKeyStream, passPhrase, armor, withIntegrityCheck).Wait();
        }

        /// <summary>
        /// Encrypt and sign the file pointed to by unencryptedFileInfo and
        /// </summary>
        /// <param name="inputFilePath"></param>
        /// <param name="outputFilePath"></param>
        /// <param name="publicKeyFilePath"></param>
        /// <param name="privateKeyFilePath"></param>
        /// <param name="passPhrase"></param>
        /// <param name="armor"></param>
        public async Task EncryptFileAndSignAsync(string inputFilePath, string outputFilePath, string publicKeyFilePath,
            string privateKeyFilePath, string passPhrase, bool armor = true, bool withIntegrityCheck = true)
        {
            await EncryptFileAndSignAsync(inputFilePath, outputFilePath, new[] { publicKeyFilePath }, privateKeyFilePath, passPhrase, armor, withIntegrityCheck);
        }

        /// <summary>
        /// Encrypt and sign the file pointed to by unencryptedFileInfo and
        /// </summary>
        /// <param name="inputFilePath"></param>
        /// <param name="outputFilePath"></param>
        /// <param name="publicKeyFilePaths"></param>
        /// <param name="privateKeyFilePath"></param>
        /// <param name="passPhrase"></param>
        /// <param name="armor"></param>
        public async Task EncryptFileAndSignAsync(string inputFilePath, string outputFilePath, IEnumerable<string> publicKeyFilePaths,
            string privateKeyFilePath, string passPhrase, bool armor = true, bool withIntegrityCheck = true)
        {
            //Avoid multiple enumerations of 'publicKeyFilePaths'
            string[] publicKeys = publicKeyFilePaths.ToArray();

            if (String.IsNullOrEmpty(inputFilePath))
                throw new ArgumentException("InputFilePath");
            if (String.IsNullOrEmpty(outputFilePath))
                throw new ArgumentException("OutputFilePath");
            if (String.IsNullOrEmpty(privateKeyFilePath))
                throw new ArgumentException("PrivateKeyFilePath");
            if (passPhrase == null)
                passPhrase = String.Empty;

            if (!File.Exists(inputFilePath))
                throw new FileNotFoundException(String.Format("Input file [{0}] does not exist.", inputFilePath));
            if (!File.Exists(privateKeyFilePath))
                throw new FileNotFoundException(String.Format("Private Key file [{0}] does not exist.", privateKeyFilePath));

            foreach (string publicKeyFilePath in publicKeys)
            {
                if (String.IsNullOrEmpty(publicKeyFilePath))
                    throw new ArgumentException(nameof(publicKeyFilePath));
                if (!File.Exists(publicKeyFilePath))
                    throw new FileNotFoundException(String.Format("Input file [{0}] does not exist.", publicKeyFilePath));
            }

            EncryptionKeys encryptionKeys = new EncryptionKeys(publicKeyFilePaths, privateKeyFilePath, passPhrase);

            if (encryptionKeys == null)
                throw new ArgumentNullException("Encryption Key not found.");

            using (Stream outputStream = File.Create(outputFilePath))
            {
                if (armor)
                {
                    using (ArmoredOutputStream armoredOutputStream = new ArmoredOutputStream(outputStream))
                    {
                        await OutputEncryptedAsync(inputFilePath, armoredOutputStream, encryptionKeys, withIntegrityCheck);
                    }
                }
                else
                    await OutputEncryptedAsync(inputFilePath, outputStream, encryptionKeys, withIntegrityCheck);
            }
        }

        /// <summary>
        /// Encrypt and sign the stream pointed to by unencryptedFileInfo and
        /// </summary>
        /// <param name="inputStream"></param>
        /// <param name="outputStream"></param>
        /// <param name="publicKeyStream"></param>
        /// <param name="privateKeyStream"></param>
        /// <param name="passPhrase"></param>
        /// <param name="armor"></param>
        public async Task EncryptStreamAndSignAsync(Stream inputStream, Stream outputStream, Stream publicKeyStream,
            Stream privateKeyStream, string passPhrase, bool armor = true, bool withIntegrityCheck = true)
        {
            await EncryptStreamAndSignAsync(inputStream, outputStream, new[] { publicKeyStream }, privateKeyStream, passPhrase, armor, withIntegrityCheck);
        }

        /// <summary>
        /// Encrypt and sign the stream pointed to by unencryptedFileInfo and
        /// </summary>
        /// <param name="inputStream"></param>
        /// <param name="outputStream"></param>
        /// <param name="publicKeyStreams"></param>
        /// <param name="privateKeyStream"></param>
        /// <param name="passPhrase"></param>
        /// <param name="armor"></param>
        public async Task EncryptStreamAndSignAsync(Stream inputStream, Stream outputStream, IEnumerable<Stream> publicKeyStreams,
            Stream privateKeyStream, string passPhrase, bool armor = true, bool withIntegrityCheck = true)
        {
            if (inputStream == null)
                throw new ArgumentException("InputStream");
            if (outputStream == null)
                throw new ArgumentException("OutputStream");
            if (privateKeyStream == null)
                throw new ArgumentException("PrivateKeyStream");
            if (passPhrase == null)
                passPhrase = String.Empty;

            foreach (Stream publicKey in publicKeyStreams)
            {
                if (publicKey == null)
                    throw new ArgumentException("PublicKeyStream");
            }

            EncryptionKeys encryptionKeys = new EncryptionKeys(publicKeyStreams, privateKeyStream, passPhrase);

            if (encryptionKeys == null)
                throw new ArgumentNullException("Encryption Key not found.");
                
            string fileName = DefaultFileName;

            if (inputStream is FileStream)
            {
                string inputFilePath = ((FileStream)inputStream).Name;
                fileName = Path.GetFileName(inputFilePath);
            }

            if (armor)
            {
                using (ArmoredOutputStream armoredOutputStream = new ArmoredOutputStream(outputStream))
                {
                    await OutputEncryptedAsync(inputStream, armoredOutputStream, encryptionKeys, withIntegrityCheck, fileName);
                }
            }
            else
                await OutputEncryptedAsync(inputStream, outputStream, encryptionKeys, withIntegrityCheck, fileName);
        }

        private async Task OutputEncryptedAsync(string inputFilePath, Stream outputStream, EncryptionKeys encryptionKeys, bool withIntegrityCheck)
        {
            using (Stream encryptedOut = ChainEncryptedOut(outputStream, encryptionKeys, withIntegrityCheck))
            {
                FileInfo unencryptedFileInfo = new FileInfo(inputFilePath);
                using (Stream compressedOut = ChainCompressedOut(encryptedOut))
                {
                    PgpSignatureGenerator signatureGenerator = InitSignatureGenerator(compressedOut, encryptionKeys);
                    using (Stream literalOut = ChainLiteralOut(compressedOut, unencryptedFileInfo))
                    {
                        using (FileStream inputFileStream = unencryptedFileInfo.OpenRead())
                        {
                            await WriteOutputAndSignAsync(compressedOut, literalOut, inputFileStream, signatureGenerator);
                            inputFileStream.Dispose();
                        }
                    }
                }
            }
        }

        private async Task OutputSignedAsync(string inputFilePath, Stream outputStream, EncryptionKeys encryptionKeys, bool withIntegrityCheck)
        {
            FileInfo unencryptedFileInfo = new FileInfo(inputFilePath);
            using (Stream compressedOut = ChainCompressedOut(outputStream))
            {
                PgpSignatureGenerator signatureGenerator = InitSignatureGenerator(compressedOut, encryptionKeys);
                using (Stream literalOut = ChainLiteralOut(compressedOut, unencryptedFileInfo))
                {
                    using (FileStream inputFileStream = unencryptedFileInfo.OpenRead())
                    {
                        await WriteOutputAndSignAsync(compressedOut, literalOut, inputFileStream, signatureGenerator);
                        inputFileStream.Dispose();
                    }
                }
            }
        }

        private async Task OutputEncryptedAsync(Stream inputStream, Stream outputStream, EncryptionKeys encryptionKeys, bool withIntegrityCheck, string name)
        {
            using (Stream encryptedOut = ChainEncryptedOut(outputStream, encryptionKeys, withIntegrityCheck))
            {
                using (Stream compressedOut = ChainCompressedOut(encryptedOut))
                {
                    PgpSignatureGenerator signatureGenerator = InitSignatureGenerator(compressedOut, encryptionKeys);
                    using (Stream literalOut = ChainLiteralStreamOut(compressedOut, inputStream, name))
                    {
                        await WriteOutputAndSignAsync(compressedOut, literalOut, inputStream, signatureGenerator);
                        inputStream.Dispose();
                    }
                }
            }
        }

        private async Task OutputSignedAsync(Stream inputStream, Stream outputStream, EncryptionKeys encryptionKeys, bool withIntegrityCheck, string name)
        {
            using (Stream compressedOut = ChainCompressedOut(outputStream))
            {
                PgpSignatureGenerator signatureGenerator = InitSignatureGenerator(compressedOut, encryptionKeys);
                using (Stream literalOut = ChainLiteralStreamOut(compressedOut, inputStream, name))
                {
                    await WriteOutputAndSignAsync(compressedOut, literalOut, inputStream, signatureGenerator);
                    inputStream.Dispose();
                }
            }
        }

        private async Task WriteOutputAndSignAsync(Stream compressedOut, Stream literalOut, FileStream inputFilePath, PgpSignatureGenerator signatureGenerator)
        {
            int length = 0;
            byte[] buf = new byte[BufferSize];
            while ((length = await inputFilePath.ReadAsync(buf, 0, buf.Length)) > 0)
            {
                await literalOut.WriteAsync(buf, 0, length);
                signatureGenerator.Update(buf, 0, length);
            }
            signatureGenerator.Generate().Encode(compressedOut);
        }

        private async Task WriteOutputAndSignAsync(Stream compressedOut, Stream literalOut, Stream inputStream, PgpSignatureGenerator signatureGenerator)
        {
            int length = 0;
            byte[] buf = new byte[BufferSize];
            while ((length = await inputStream.ReadAsync(buf, 0, buf.Length)) > 0)
            {
                await literalOut.WriteAsync(buf, 0, length);
                signatureGenerator.Update(buf, 0, length);
            }
            signatureGenerator.Generate().Encode(compressedOut);
        }

        private Stream ChainEncryptedOut(Stream outputStream, EncryptionKeys encryptionKeys, bool withIntegrityCheck)
        {
            PgpEncryptedDataGenerator encryptedDataGenerator;
            encryptedDataGenerator = new PgpEncryptedDataGenerator(SymmetricKeyAlgorithm, withIntegrityCheck, new SecureRandom());

            if (encryptionKeys.PublicKey != null)
            {
                encryptedDataGenerator.AddMethod(encryptionKeys.PublicKey);
            }
            else if (encryptionKeys.PublicKeys != null)
            {
                foreach (PgpPublicKey publicKey in encryptionKeys.PublicKeys)
                {
                    encryptedDataGenerator.AddMethod(publicKey);
                }
            }
            
            return encryptedDataGenerator.Open(outputStream, new byte[BufferSize]);
        }

        private Stream ChainCompressedOut(Stream encryptedOut)
        {
            if (CompressionAlgorithm != CompressionAlgorithmTag.Uncompressed)
            {
                PgpCompressedDataGenerator compressedDataGenerator = new PgpCompressedDataGenerator(CompressionAlgorithmTag.Zip);
                return compressedDataGenerator.Open(encryptedOut);
            }
            else
                return encryptedOut;
        }

        private Stream ChainLiteralOut(Stream compressedOut, FileInfo file)
        {
            PgpLiteralDataGenerator pgpLiteralDataGenerator = new PgpLiteralDataGenerator();
            return pgpLiteralDataGenerator.Open(compressedOut, FileTypeToChar(), file.Name, file.Length, DateTime.Now);
        }

        private Stream ChainLiteralStreamOut(Stream compressedOut, Stream inputStream, string name)
        {
            PgpLiteralDataGenerator pgpLiteralDataGenerator = new PgpLiteralDataGenerator();
            return pgpLiteralDataGenerator.Open(compressedOut, FileTypeToChar(), name, inputStream.Length, DateTime.Now);
        }

        private PgpSignatureGenerator InitSignatureGenerator(Stream compressedOut, EncryptionKeys encryptionKeys)
        {
            PublicKeyAlgorithmTag tag = encryptionKeys.SecretKey.PublicKey.Algorithm;
            PgpSignatureGenerator pgpSignatureGenerator = new PgpSignatureGenerator(tag, HashAlgorithmTag);
            pgpSignatureGenerator.InitSign(PgpSignature.BinaryDocument, encryptionKeys.PrivateKey);
            foreach (string userId in encryptionKeys.SecretKey.PublicKey.GetUserIds())
            {
                PgpSignatureSubpacketGenerator subPacketGenerator = new PgpSignatureSubpacketGenerator();
                subPacketGenerator.SetSignerUserId(false, userId);
                pgpSignatureGenerator.SetHashedSubpackets(subPacketGenerator.Generate());
                // Just the first one!
                break;
            }
            pgpSignatureGenerator.GenerateOnePassVersion(false).Encode(compressedOut);
            return pgpSignatureGenerator;
        }

        #endregion Encrypt and Sign

        #region Sign

        public void SignFile(string inputFilePath, string outputFilePath,
            string privateKeyFilePath, string passPhrase, bool armor = true, bool withIntegrityCheck = true)
        {
            SignFileAsync(inputFilePath, outputFilePath, privateKeyFilePath, passPhrase, armor, withIntegrityCheck).Wait();
        }

        /// <summary>
        /// Sign the file pointed to by unencryptedFileInfo and
        /// </summary>
        /// <param name="inputFilePath"></param>
        /// <param name="outputFilePath"></param>
        /// <param name="privateKeyFilePath"></param>
        /// <param name="passPhrase"></param>
        /// <param name="armor"></param>
        public async Task SignFileAsync(string inputFilePath, string outputFilePath,
            string privateKeyFilePath, string passPhrase, bool armor = true, bool withIntegrityCheck = true)
        {
            if (String.IsNullOrEmpty(inputFilePath))
                throw new ArgumentException("InputFilePath");
            if (String.IsNullOrEmpty(outputFilePath))
                throw new ArgumentException("OutputFilePath");
            if (String.IsNullOrEmpty(privateKeyFilePath))
                throw new ArgumentException("PrivateKeyFilePath");
            if (passPhrase == null)
                passPhrase = String.Empty;

            if (!File.Exists(inputFilePath))
                throw new FileNotFoundException(String.Format("Input file [{0}] does not exist.", inputFilePath));
            if (!File.Exists(privateKeyFilePath))
                throw new FileNotFoundException(String.Format("Private Key file [{0}] does not exist.", privateKeyFilePath));

            EncryptionKeys encryptionKeys = new EncryptionKeys(privateKeyFilePath, passPhrase);

            if (encryptionKeys == null)
                throw new ArgumentNullException("Encryption Key not found.");

            using (Stream outputStream = File.Create(outputFilePath))
            {
                if (armor)
                {
                    using (ArmoredOutputStream armoredOutputStream = new ArmoredOutputStream(outputStream))
                    {
                        await OutputSignedAsync(inputFilePath, armoredOutputStream, encryptionKeys, withIntegrityCheck);
                    }
                }
                else
                    await OutputSignedAsync(inputFilePath, outputStream, encryptionKeys, withIntegrityCheck);
            }
        }

        public void SignStream(Stream inputStream, Stream outputStream,
            Stream privateKeyStream, string passPhrase, bool armor = true, bool withIntegrityCheck = true)
        {
            SignStreamAsync(inputStream, outputStream, privateKeyStream, passPhrase, armor, withIntegrityCheck).Wait();
        }

        /// <summary>
        /// Sign the stream pointed to by unencryptedFileInfo and
        /// </summary>
        /// <param name="inputStream"></param>
        /// <param name="outputStream"></param>
        /// <param name="publicKeyStreams"></param>
        /// <param name="privateKeyStream"></param>
        /// <param name="passPhrase"></param>
        /// <param name="armor"></param>
        public async Task SignStreamAsync(Stream inputStream, Stream outputStream,
            Stream privateKeyStream, string passPhrase, bool armor = true, bool withIntegrityCheck = true)
        {
            if (inputStream == null)
                throw new ArgumentException("InputStream");
            if (outputStream == null)
                throw new ArgumentException("OutputStream");
            if (privateKeyStream == null)
                throw new ArgumentException("PrivateKeyStream");
            if (passPhrase == null)
                passPhrase = String.Empty;

            EncryptionKeys encryptionKeys = new EncryptionKeys(privateKeyStream, passPhrase);

            if (encryptionKeys == null)
                throw new ArgumentNullException("Encryption Key not found.");
                
            string fileName = DefaultFileName;

            if (inputStream is FileStream)
            {
                string inputFilePath = ((FileStream)inputStream).Name;
                fileName = Path.GetFileName(inputFilePath);
            }

            if (armor)
            {
                using (ArmoredOutputStream armoredOutputStream = new ArmoredOutputStream(outputStream))
                {
                    await OutputSignedAsync(inputStream, armoredOutputStream, encryptionKeys, withIntegrityCheck, fileName);
                }
            }
            else
                await OutputSignedAsync(inputStream, outputStream, encryptionKeys, withIntegrityCheck, fileName);
        }

        #endregion Sign

        #region Decrypt

        public void DecryptFile(string inputFilePath, string outputFilePath, string privateKeyFilePath, string passPhrase)
        {
            DecryptFileAsync(inputFilePath, outputFilePath, privateKeyFilePath, passPhrase).Wait();
        }

        public void DecryptStream(Stream inputStream, Stream outputStream, Stream privateKeyStream, string passPhrase)
        {
            DecryptStreamAsync(inputStream, outputStream, privateKeyStream, passPhrase).Wait();
        }

        /// <summary>
        /// PGP decrypt a given file.
        /// </summary>
        /// <param name="inputFilePath"></param>
        /// <param name="outputFilePath"></param>
        /// <param name="privateKeyFilePath"></param>
        /// <param name="passPhrase"></param>
        public async Task DecryptFileAsync(string inputFilePath, string outputFilePath, string privateKeyFilePath, string passPhrase)
        {
            if (String.IsNullOrEmpty(inputFilePath))
                throw new ArgumentException("InputFilePath");
            if (String.IsNullOrEmpty(outputFilePath))
                throw new ArgumentException("OutputFilePath");
            if (String.IsNullOrEmpty(privateKeyFilePath))
                throw new ArgumentException("PrivateKeyFilePath");
            if (passPhrase == null)
                passPhrase = String.Empty;

            if (!File.Exists(inputFilePath))
                throw new FileNotFoundException(String.Format("Encrypted File [{0}] not found.", inputFilePath));
            if (!File.Exists(privateKeyFilePath))
                throw new FileNotFoundException(String.Format("Private Key File [{0}] not found.", privateKeyFilePath));

            using (Stream inputStream = File.OpenRead(inputFilePath))
            {
                using (Stream keyStream = File.OpenRead(privateKeyFilePath))
                {
                    using (Stream outStream = File.Create(outputFilePath))
                        await DecryptAsync(inputStream, outStream, keyStream, passPhrase);
                }
            }
        }

        /// <summary>
        /// PGP decrypt a given stream.
        /// </summary>
        /// <param name="inputStream"></param>
        /// <param name="outputStream"></param>
        /// <param name="privateKeyFilePath"></param>
        /// <param name="passPhrase"></param>
        public async Task<Stream> DecryptStreamAsync(Stream inputStream, Stream outputStream, Stream privateKeyStream, string passPhrase)
        {
            if (inputStream == null)
                throw new ArgumentException("InputStream");
            if (outputStream == null)
                throw new ArgumentException("OutputStream");
            if (privateKeyStream == null)
                throw new ArgumentException("PrivateKeyFilePath");
            if (passPhrase == null)
                passPhrase = String.Empty;

            await DecryptAsync(inputStream, outputStream, privateKeyStream, passPhrase);
            return outputStream;
        }

        /*
        * PGP decrypt a given stream.
        */
        private async Task DecryptAsync(Stream inputStream, Stream outputStream, Stream privateKeyStream, string passPhrase)
        {
            if (inputStream == null)
                throw new ArgumentException("InputStream");
            if (outputStream == null)
                throw new ArgumentException("outputStream");
            if (privateKeyStream == null)
                throw new ArgumentException("privateKeyStream");
            if (passPhrase == null)
                passPhrase = String.Empty;

            PgpObjectFactory objFactory = new PgpObjectFactory(PgpUtilities.GetDecoderStream(inputStream));
            // find secret key
            PgpSecretKeyRingBundle pgpSec = new PgpSecretKeyRingBundle(PgpUtilities.GetDecoderStream(privateKeyStream));

            PgpObject obj = null;
            if (objFactory != null)
                obj = objFactory.NextPgpObject();

            // the first object might be a PGP marker packet.
            PgpEncryptedDataList enc = null;
            if (obj is PgpEncryptedDataList)
                enc = (PgpEncryptedDataList)obj;
            else
                enc = (PgpEncryptedDataList)objFactory.NextPgpObject();

            // decrypt
            PgpPrivateKey privateKey = null;
            PgpPublicKeyEncryptedData pbe = null;
            foreach (PgpPublicKeyEncryptedData pked in enc.GetEncryptedDataObjects())
            {
                privateKey = FindSecretKey(pgpSec, pked.KeyId, passPhrase.ToCharArray());

                if (privateKey != null)
                {
                    pbe = pked;
                    break;
                }
            }

            if (privateKey == null)
                throw new ArgumentException("Secret key for message not found.");

            PgpObjectFactory plainFact = null;

            using (Stream clear = pbe.GetDataStream(privateKey))
            {
                plainFact = new PgpObjectFactory(clear);
            }

            PgpObject message = plainFact.NextPgpObject();

            if (message is PgpOnePassSignatureList)
            {
                message = plainFact.NextPgpObject();
            }

            if (message is PgpCompressedData)
            {
                PgpCompressedData cData = (PgpCompressedData)message;
                PgpObjectFactory of = null;

                using (Stream compDataIn = cData.GetDataStream())
                {
                    of = new PgpObjectFactory(compDataIn);
                }

                message = of.NextPgpObject();
                if (message is PgpOnePassSignatureList)
                {
                    message = of.NextPgpObject();
                    PgpLiteralData Ld = null;
                    Ld = (PgpLiteralData)message;
                    Stream unc = Ld.GetInputStream();
                    await Streams.PipeAllAsync(unc, outputStream);
                }
                else
                {
                    PgpLiteralData Ld = null;
                    Ld = (PgpLiteralData)message;
                    Stream unc = Ld.GetInputStream();
                    await Streams.PipeAllAsync(unc, outputStream);
                }
            }
            else if (message is PgpLiteralData)
            {
                PgpLiteralData ld = (PgpLiteralData)message;
                string outFileName = ld.FileName;

                Stream unc = ld.GetInputStream();
                await Streams.PipeAllAsync(unc, outputStream);

                if (pbe.IsIntegrityProtected())
                {
                    if (!pbe.Verify())
                    {
                        throw new PgpException("Message failed integrity check.");
                    }
                }
            }
            else if (message is PgpOnePassSignatureList)
                throw new PgpException("Encrypted message contains a signed message - not literal data.");
            else
                throw new PgpException("Message is not a simple encrypted file.");
        }

        #endregion Decrypt

        #region DecryptAndVerify

        public void DecryptFileAndVerify(string inputFilePath, string outputFilePath, string publicKeyFilePath, string privateKeyFilePath, string passPhrase)
        {
            try
            {
                DecryptFileAndVerifyAsync(inputFilePath, outputFilePath, publicKeyFilePath, privateKeyFilePath, passPhrase).Wait();
            }
            catch (Exception ex)
            {
                throw ex.InnerException;
            }
        }

        public void DecryptStreamAndAndVerify(Stream inputStream, Stream outputStream, Stream publicKeyStream, Stream privateKeyStream, string passPhrase)
        {
            try
            {
                DecryptStreamAndVerifyAsync(inputStream, outputStream, publicKeyStream, privateKeyStream, passPhrase).Wait();
            }
            catch (Exception ex)
            {
                throw ex.InnerException;
            }
        }

        public bool VerifyFile(string inputFilePath, string publicKeyFilePath)
        {
            return VerifyFileAsync(inputFilePath, publicKeyFilePath).Result;
        }

        public bool VerifyStream(Stream inputStream, Stream publicKeyStream)
        {
            return VerifyStreamAsync(inputStream, publicKeyStream).Result;
        }

        /// <summary>
        /// PGP decrypt and verify a given file.
        /// </summary>
        /// <param name="inputFilePath"></param>
        /// <param name="outputFilePath"></param>
        /// <param name="publicKeyFilePath"></param>
        /// <param name="privateKeyFilePath"></param>
        /// <param name="passPhrase"></param>
        public async Task DecryptFileAndVerifyAsync(string inputFilePath, string outputFilePath, string publicKeyFilePath, string privateKeyFilePath, string passPhrase)
        {
            if (String.IsNullOrEmpty(inputFilePath))
                throw new ArgumentException("InputFilePath");
            if (String.IsNullOrEmpty(outputFilePath))
                throw new ArgumentException("OutputFilePath");
            if (String.IsNullOrEmpty(publicKeyFilePath))
                throw new ArgumentException("PublicKeyFilePath");
            if (String.IsNullOrEmpty(privateKeyFilePath))
                throw new ArgumentException("PrivateKeyFilePath");
            if (passPhrase == null)
                passPhrase = String.Empty;

            if (!File.Exists(inputFilePath))
                throw new FileNotFoundException(String.Format("Encrypted File [{0}] not found.", inputFilePath));
            if (!File.Exists(publicKeyFilePath))
                throw new FileNotFoundException(String.Format("Public Key File [{0}] not found.", publicKeyFilePath));
            if (!File.Exists(privateKeyFilePath))
                throw new FileNotFoundException(String.Format("Private Key File [{0}] not found.", privateKeyFilePath));

            using (Stream inputStream = File.OpenRead(inputFilePath))
            {
                using (Stream publicKeyStream = File.OpenRead(publicKeyFilePath))
                using (Stream privateKeyStream = File.OpenRead(privateKeyFilePath))
                using (Stream outStream = File.Create(outputFilePath))
                    await DecryptAndVerifyAsync(inputStream, outStream, publicKeyStream, privateKeyStream, passPhrase);
            }
        }

        /// <summary>
        /// PGP decrypt and verify a given stream.
        /// </summary>
        /// <param name="inputStream"></param>
        /// <param name="outputStream"></param>
        /// <param name="privateKeyFilePath"></param>
        /// <param name="passPhrase"></param>
        public async Task<Stream> DecryptStreamAndVerifyAsync(Stream inputStream, Stream outputStream, Stream publicKeyStream, Stream privateKeyStream, string passPhrase)
        {
            if (inputStream == null)
                throw new ArgumentException("InputStream");
            if (outputStream == null)
                throw new ArgumentException("OutputStream");
            if (publicKeyStream == null)
                throw new ArgumentException("PublicKeyFileStream");
            if (privateKeyStream == null)
                throw new ArgumentException("PrivateKeyFileStream");
            if (passPhrase == null)
                passPhrase = String.Empty;

            await DecryptAndVerifyAsync(inputStream, outputStream, publicKeyStream, privateKeyStream, passPhrase);
            return outputStream;
        }

        /*
        * PGP decrypt and verify a given stream.
        */
        private async Task DecryptAndVerifyAsync(Stream inputStream, Stream outputStream, Stream publicKeyStream, Stream privateKeyStream, string passPhrase)
        {
            // find secret key
            PgpSecretKeyRingBundle pgpSec = new PgpSecretKeyRingBundle(PgpUtilities.GetDecoderStream(privateKeyStream));
            PgpEncryptedDataList encryptedDataList = Utilities.GetEncryptedDataList(PgpUtilities.GetDecoderStream(inputStream));

            // decrypt
            PgpPrivateKey privateKey = null;
            PgpPublicKeyEncryptedData pbe = null;
            foreach (PgpPublicKeyEncryptedData pked in encryptedDataList.GetEncryptedDataObjects())
            {
                privateKey = FindSecretKey(pgpSec, pked.KeyId, passPhrase.ToCharArray());

                if (privateKey != null)
                {
                    pbe = pked;
                    break;
                }
            }

            if (privateKey == null)
                throw new ArgumentException("Secret key for message not found.");

            var publicKey = Utilities.ReadPublicKey(publicKeyStream);

            PgpObjectFactory plainFact = null;
            using (Stream clear = pbe.GetDataStream(privateKey))
            {
                plainFact = new PgpObjectFactory(clear);
            }

            PgpObject message = plainFact.NextPgpObject();

            if (message is PgpCompressedData cData)
            {
                using (Stream compDataIn = cData.GetDataStream())
                {
                    plainFact = new PgpObjectFactory(compDataIn);
                }

                message = plainFact.NextPgpObject();
            }

            if (message is PgpOnePassSignatureList pgpOnePassSignatureList)
            {
                PgpOnePassSignature pgpOnePassSignature = pgpOnePassSignatureList[0];

                var verified = publicKey.KeyId == pgpOnePassSignature.KeyId || publicKey.GetKeySignatures().Cast<PgpSignature>().Select(x => x.KeyId).Contains(pgpOnePassSignature.KeyId);
                if (verified == false)
                    throw new PgpException("Failed to verify file.");

                message = plainFact.NextPgpObject();
            }
            else
                throw new PgpException("File was not signed.");

            if (message is PgpLiteralData ld)
            {
                Stream unc = ld.GetInputStream();
                await Streams.PipeAllAsync(unc, outputStream);

                if (pbe.IsIntegrityProtected())
                {
                    if (!pbe.Verify())
                    {
                        throw new PgpException("Message failed integrity check.");
                    }
                }
            }
            else
                throw new PgpException("Message is not a simple encrypted file.");
        }

        /// <summary>
        /// PGP verify a given file.
        /// </summary>
        /// <param name="inputFilePath"></param>
        /// <param name="publicKeyFilePath"></param>
        public async Task<bool> VerifyFileAsync(string inputFilePath, string publicKeyFilePath)
        {
            if (String.IsNullOrEmpty(inputFilePath))
                throw new ArgumentException("InputFilePath");
            if (String.IsNullOrEmpty(publicKeyFilePath))
                throw new ArgumentException("PublicKeyFilePath");

            if (!File.Exists(inputFilePath))
                throw new FileNotFoundException(String.Format("Encrypted File [{0}] not found.", inputFilePath));
            if (!File.Exists(publicKeyFilePath))
                throw new FileNotFoundException(String.Format("Public Key File [{0}] not found.", publicKeyFilePath));

            using (Stream inputStream = File.OpenRead(inputFilePath))
            using (Stream publicKeyStream = File.OpenRead(publicKeyFilePath))
                return await VerifyAsync(inputStream, publicKeyStream);
        }

        /// <summary>
        /// PGP verify a given stream.
        /// </summary>
        /// <param name="inputStream"></param>
        /// <param name="publicKeyStream"></param>
        public async Task<bool> VerifyStreamAsync(Stream inputStream, Stream publicKeyStream)
        {
            if (inputStream == null)
                throw new ArgumentException("InputStream");
            if (publicKeyStream == null)
                throw new ArgumentException("PublicKeyStream");

            return await VerifyAsync(inputStream, publicKeyStream);
        }

        private async Task<bool> VerifyAsync(Stream inputStream, Stream publicKeyStream)
        {
            PgpPublicKey publicKey = Utilities.ReadPublicKey(publicKeyStream);
            bool verified = false;

            System.IO.Stream encodedFile = PgpUtilities.GetDecoderStream(inputStream);
            PgpObjectFactory factory = new PgpObjectFactory(encodedFile);
            PgpObject pgpObject = factory.NextPgpObject();

            if (pgpObject is PgpCompressedData)
            {
                PgpPublicKeyEncryptedData publicKeyED = Utilities.ExtractPublicKeyEncryptedData(encodedFile);

                // Verify against public key ID and that of any sub keys
                if (publicKey.KeyId == publicKeyED.KeyId || publicKey.GetKeySignatures().Cast<PgpSignature>().Select(x => x.KeyId).Contains(publicKeyED.KeyId))
                {
                    verified = true;
                }
                else
                {
                    verified = false;
                }
            }
            else if (pgpObject is PgpEncryptedDataList)
            {
                PgpEncryptedDataList encryptedDataList = (PgpEncryptedDataList)pgpObject;
                PgpPublicKeyEncryptedData publicKeyED = Utilities.ExtractPublicKey(encryptedDataList);

                // Verify against public key ID and that of any sub keys
                if (publicKey.KeyId == publicKeyED.KeyId || publicKey.GetKeySignatures().Cast<PgpSignature>().Select(x => x.KeyId).Contains(publicKeyED.KeyId))
                {
                    verified = true;
                }
                else
                {
                    verified = false;
                }
            }
            else if (pgpObject is PgpOnePassSignatureList)
            {
                PgpOnePassSignatureList pgpOnePassSignatureList = (PgpOnePassSignatureList)pgpObject;
                PgpOnePassSignature pgpOnePassSignature = pgpOnePassSignatureList[0];

                // Verify against public key ID and that of any sub keys
                if (publicKey.KeyId == pgpOnePassSignature.KeyId || publicKey.GetKeySignatures().Cast<PgpSignature>().Select(x => x.KeyId).Contains(pgpOnePassSignature.KeyId))
                {
                    verified = true;
                }
                else
                {
                    verified = false;
                }
            }
            else
                throw new PgpException("Message is not a encrypted and signed file or simple signed file.");

            return verified;
        }

        #endregion DecryptAndVerify

        #region GenerateKey

        public async Task GenerateKeyAsync(string publicKeyFilePath, string privateKeyFilePath, string username = null, string password = null, int strength = 1024, int certainty = 8)
        {
            await Task.Run(() => GenerateKey(publicKeyFilePath, privateKeyFilePath, username, password, strength, certainty));
        }

        public void GenerateKey(string publicKeyFilePath, string privateKeyFilePath, string username = null, string password = null, int strength = 1024, int certainty = 8)
        {
            if (String.IsNullOrEmpty(publicKeyFilePath))
                throw new ArgumentException("PublicKeyFilePath");
            if (String.IsNullOrEmpty(privateKeyFilePath))
                throw new ArgumentException("PrivateKeyFilePath");

            using (Stream pubs = File.Open(publicKeyFilePath, FileMode.Create))
            using (Stream pris = File.Open(privateKeyFilePath, FileMode.Create))
                GenerateKey(pubs, pris, username, password, strength, certainty);
        }

        public void GenerateKey(Stream publicKeyStream, Stream privateKeyStream, string username = null, string password = null, int strength = 1024, int certainty = 8, bool armor = true)
        {
            username = username == null ? string.Empty : username;
            password = password == null ? string.Empty : password;

            IAsymmetricCipherKeyPairGenerator kpg = new RsaKeyPairGenerator();
            kpg.Init(new RsaKeyGenerationParameters(BigInteger.ValueOf(0x13), new SecureRandom(), strength, certainty));
            AsymmetricCipherKeyPair kp = kpg.GenerateKeyPair();

            ExportKeyPair(privateKeyStream, publicKeyStream, kp.Public, kp.Private, username, password.ToCharArray(), armor);
        }

        #endregion GenerateKey

        #region Private helpers

        private char FileTypeToChar()
        {
            if (FileType == PGPFileType.UTF8)
                return PgpLiteralData.Utf8;
            else if (FileType == PGPFileType.Text)
                return PgpLiteralData.Text;
            else
                return PgpLiteralData.Binary;

        }

        private void ExportKeyPair(
                    Stream secretOut,
                    Stream publicOut,
                    AsymmetricKeyParameter publicKey,
                    AsymmetricKeyParameter privateKey,
                    string identity,
                    char[] passPhrase,
                    bool armor)
        {
            if (secretOut == null)
                throw new ArgumentException("secretOut");
            if (publicOut == null)
                throw new ArgumentException("publicOut");

            if (armor)
            {
                secretOut = new ArmoredOutputStream(secretOut);
            }

            PgpSecretKey secretKey = new PgpSecretKey(
                PgpSignatureType,
                PublicKeyAlgorithm,
                publicKey,
                privateKey,
                DateTime.Now,
                identity,
                SymmetricKeyAlgorithm,
                passPhrase,
                null,
                null,
                new SecureRandom()
                //                ,"BC"
                );

                secretKey.Encode(secretOut);

            secretOut.Dispose();

            if (armor)
            {
                publicOut = new ArmoredOutputStream(publicOut);
            }

            PgpPublicKey key = secretKey.PublicKey;

            key.Encode(publicOut);

            publicOut.Dispose();
        }
        
        /*
        * Search a secret key ring collection for a secret key corresponding to keyId if it exists.
        */
        private PgpPrivateKey FindSecretKey(PgpSecretKeyRingBundle pgpSec, long keyId, char[] pass)
        {
            PgpSecretKey pgpSecKey = pgpSec.GetSecretKey(keyId);

            if (pgpSecKey == null)
                return null;

            return pgpSecKey.ExtractPrivateKey(pass);
        }

        public void Dispose()
        {
        }

        #endregion Private helpers
    }
}

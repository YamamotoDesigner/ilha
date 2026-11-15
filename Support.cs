using System;
using System.IO;
using System.Text;
using System.Windows.Media.Imaging;
using QRCoder;

namespace Ilha
{
    /// <summary>Dados de apoio ao projeto (Pix).</summary>
    internal static class Support
    {
        /// <summary>
        /// Código "Pix copia e cola" (BR Code) de quem recebe as doações.
        /// Vazio = as opções de apoio ficam escondidas no app.
        /// </summary>
        public const string PixCode =
            "00020126580014BR.GOV.BCB.PIX0136f2e5c3e0-6482-4443-8a85-f46d3a2b10095204000053039865802BR5925Lucas Yamamoto de Lira Sa6009SAO PAULO62140510AZNMQsWz2T630470BA";

        public static bool IsConfigured => PixCode.Length > 0;

        /// <summary>QR code do Pix como imagem para WPF.</summary>
        public static BitmapImage CreateQr(string payload)
        {
            using var generator = new QRCodeGenerator();
            using var data = generator.CreateQrCode(payload, QRCodeGenerator.ECCLevel.M);
            byte[] png = new PngByteQRCode(data).GetGraphic(12);
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = new MemoryStream(png);
            image.EndInit();
            image.Freeze();
            return image;
        }

        /// <summary>
        /// Confere o CRC16 do BR Code (os 4 últimos caracteres). Pega código colado pela metade.
        /// </summary>
        public static bool IsValidPix(string code)
        {
            if (code.Length < 20 || !code.StartsWith("000201", StringComparison.Ordinal)) return false;
            int crcAt = code.LastIndexOf("6304", StringComparison.Ordinal);
            if (crcAt < 0 || crcAt + 8 != code.Length) return false;
            ushort crc = 0xFFFF;
            foreach (byte b in Encoding.UTF8.GetBytes(code[..(crcAt + 4)]))
            {
                crc ^= (ushort)(b << 8);
                for (int i = 0; i < 8; i++)
                    crc = (crc & 0x8000) != 0 ? (ushort)((crc << 1) ^ 0x1021) : (ushort)(crc << 1);
            }
            return string.Equals(crc.ToString("X4"), code[(crcAt + 4)..], StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Nome de quem recebe, lido do próprio código (campo 59).</summary>
        public static string? ReceiverName(string code)
        {
            int i = 0;
            while (i + 4 <= code.Length)
            {
                string id = code.Substring(i, 2);
                if (!int.TryParse(code.AsSpan(i + 2, 2), out int len) || i + 4 + len > code.Length) return null;
                if (id == "59") return code.Substring(i + 4, len);
                i += 4 + len;
            }
            return null;
        }
    }
}

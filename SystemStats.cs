using System;
using System.Runtime.InteropServices;

namespace Ilha
{
    /// <summary>Uso de processador e memória do PC, com um histórico curto para os gráficos.</summary>
    internal sealed class SystemStats
    {
        public const int HistoryLength = 40;

        private ulong _lastIdle, _lastKernel, _lastUser;
        private bool _hasLast;

        /// <summary>Uso do processador (0–1), do mais antigo para o mais recente.</summary>
        public double[] Cpu { get; } = new double[HistoryLength];
        /// <summary>Memória em uso (0–1), do mais antigo para o mais recente.</summary>
        public double[] Memory { get; } = new double[HistoryLength];

        public double CpuNow => Cpu[^1];
        public double MemoryNow => Memory[^1];
        public double MemoryUsedGb { get; private set; }

        /// <summary>Lê os valores atuais e empurra o histórico uma posição.</summary>
        public void Sample()
        {
            _samples++;
            Push(Cpu, ReadCpu());
            Push(Memory, ReadMemory());
        }

        private int _samples;

        private void Push(double[] history, double value)
        {
            value = Math.Clamp(value, 0, 1);
            // A primeira leitura de CPU é só a base; até a segunda, o gráfico começa no valor real, não em zero.
            if (_samples <= 2)
            {
                Array.Fill(history, value);
                return;
            }
            Array.Copy(history, 1, history, 0, history.Length - 1);
            history[^1] = value;
        }

        private double ReadCpu()
        {
            if (!GetSystemTimes(out var idleFt, out var kernelFt, out var userFt)) return CpuNow;
            ulong idle = idleFt.Value, kernel = kernelFt.Value, user = userFt.Value;
            double result = CpuNow;
            if (_hasLast)
            {
                // O tempo de kernel já inclui o tempo ocioso.
                ulong total = (kernel - _lastKernel) + (user - _lastUser);
                ulong idleDelta = idle - _lastIdle;
                if (total > 0) result = 1 - idleDelta / (double)total;
            }
            _lastIdle = idle;
            _lastKernel = kernel;
            _lastUser = user;
            _hasLast = true;
            return result;
        }

        private double ReadMemory()
        {
            var status = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
            if (!GlobalMemoryStatusEx(ref status) || status.TotalPhys == 0) return MemoryNow;
            ulong used = status.TotalPhys - status.AvailPhys;
            MemoryUsedGb = used / 1024.0 / 1024 / 1024;
            return used / (double)status.TotalPhys;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct FileTime
        {
            public uint Low, High;
            public ulong Value => ((ulong)High << 32) | Low;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MemoryStatusEx
        {
            public uint Length, MemoryLoad;
            public ulong TotalPhys, AvailPhys, TotalPageFile, AvailPageFile, TotalVirtual, AvailVirtual, AvailExtendedVirtual;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetSystemTimes(out FileTime idle, out FileTime kernel, out FileTime user);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx status);
    }
}

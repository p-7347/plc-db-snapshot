using PlcDbSnapshot.Core;
using S7CommPlusDriver;
using S7CommPlusDriver.ClientApi;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace PlcDbSnapshot.App
{
    /// <summary>One readable block on the PLC: a DB, or the I/Q/M area.</summary>
    public sealed class PlcBlock
    {
        public string Name { get; set; } = "";
        /// <summary>DB number, or 0 for the I/Q/M areas.</summary>
        public uint Number { get; set; }
        /// <summary>First segment of each member's access sequence (hex relation id).</summary>
        public string AccessKey { get; set; } = "";
        public bool IsArea => Number == 0;
        public int TagCount { get; set; }

        public string Display
        {
            get
            {
                var head = IsArea ? "[영역] " + Name : "DB" + Number + "  " + Name;
                return TagCount > 0 ? head + "  (" + TagCount + ")" : head;
            }
        }
    }

    /// <summary>
    /// READ-ONLY access to a live S7-1200/1500 CPU over S7CommPlus (TCP 102), via the vendored
    /// S7CommPlusDriver. This class deliberately has no write method.
    /// Not thread-safe: call it from one worker at a time.
    /// </summary>
    public sealed class PlcReader : IDisposable
    {
        private const int ReadChunk = 400;

        private S7CommPlusConnection _conn;
        private Dictionary<string, List<VarInfo>> _varsByAccessKey;
        private Dictionary<string, VarInfo> _varsByName;

        public string Address { get; private set; } = "";
        public bool IsConnected => _conn != null;

        public void Connect(string address, string password)
        {
            if (_conn != null) throw new InvalidOperationException("이미 접속되어 있습니다.");
            var conn = new S7CommPlusConnection();
            var res = conn.Connect(address, password ?? "", "");
            if (res != 0)
            {
                throw new InvalidOperationException("PLC 접속 실패 (" + address + ", 오류 코드 0x" + res.ToString("X", CultureInfo.InvariantCulture) + ").\n" + ConnectHint(res));
            }
            _conn = conn;
            Address = address;
        }

        private static string ConnectHint(int code)
        {
            switch (code)
            {
                case S7Consts.errTCPSocketCreation:
                case S7Consts.errTCPConnectionTimeout:
                case S7Consts.errTCPConnectionFailed:
                case S7Consts.errTCPUnreachableHost:
                    return "PLC에 네트워크로 닿지 않습니다. IP 주소, 랜선, 노트북 IP 대역(같은 서브넷인지), ping 응답을 확인하세요.";
                case S7Consts.errTCPReceiveTimeout:
                case S7Consts.errTCPConnectionReset:
                case S7Consts.errIsoConnect:
                    return "PLC가 응답하다 끊었습니다. S7-1500 FW V2.9 / S7-1200 FW V4.3 이상인지, 다른 접속이 너무 많지 않은지 확인하세요.";
                case S7Consts.errOpenSSL:
                    return "PLC까지는 닿았지만 TLS(OpenSSL) 시작에 실패했습니다. exe 옆에 libssl-3-x64.dll, libcrypto-3-x64.dll이 있는지 확인하세요.";
                case S7Consts.errCliNeedPassword:
                case S7Consts.errCliInvalidPassword:
                case S7Consts.errCliAccessDenied:
                    return "PLC 접근 보호 비밀번호가 필요하거나 틀렸습니다.";
                default:
                    return "PLC 접근 보호 비밀번호, CPU 펌웨어 버전(S7-1500 V2.9 / S7-1200 V4.3 이상)을 확인하세요.";
            }
        }

        public void Disconnect()
        {
            var conn = _conn;
            _conn = null;
            _varsByAccessKey = null;
            _varsByName = null;
            if (conn != null)
            {
                try { conn.Disconnect(); } catch { /* connection already gone */ }
            }
        }

        public void Dispose() => Disconnect();

        private S7CommPlusConnection Conn => _conn ?? throw new InvalidOperationException("PLC에 접속되어 있지 않습니다.");

        /// <summary>
        /// Reads the full symbol table once (all DBs and I/Q/M) and returns the block list.
        /// On a big program this can take a while; the result is cached for the connection.
        /// </summary>
        public List<PlcBlock> LoadBlocks()
        {
            var res = Conn.GetListOfDatablocks(out var dbList);
            if (res != 0) throw new InvalidOperationException("DB 목록 읽기 실패 (오류 코드 " + res + ").");

            res = Conn.Browse(out var vars);
            if (res != 0) throw new InvalidOperationException("태그 목록(Browse) 읽기 실패 (오류 코드 " + res + ").");

            _varsByAccessKey = vars
                .GroupBy(v => AccessKeyOf(v.AccessSequence), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);
            _varsByName = new Dictionary<string, VarInfo>(StringComparer.Ordinal);
            foreach (var v in vars) _varsByName[v.Name] = v;

            var blocks = dbList
                .Select(db => new PlcBlock
                {
                    Name = db.db_name,
                    Number = db.db_number,
                    AccessKey = db.db_block_relid.ToString("X", CultureInfo.InvariantCulture)
                })
                .OrderBy(b => b.Number)
                .ToList();

            blocks.Add(Area("I (입력)", Ids.NativeObjects_theIArea_Rid));
            blocks.Add(Area("Q (출력)", Ids.NativeObjects_theQArea_Rid));
            blocks.Add(Area("M (메모리)", Ids.NativeObjects_theMArea_Rid));

            foreach (var b in blocks)
            {
                b.TagCount = _varsByAccessKey.TryGetValue(b.AccessKey, out var list) ? list.Count : 0;
            }
            return blocks.Where(b => b.TagCount > 0).ToList();
        }

        private static PlcBlock Area(string name, int rid)
        {
            return new PlcBlock { Name = name, Number = 0, AccessKey = rid.ToString("X", CultureInfo.InvariantCulture) };
        }

        private static string AccessKeyOf(string accessSequence)
        {
            if (string.IsNullOrEmpty(accessSequence)) return "";
            var dot = accessSequence.IndexOf('.');
            return dot < 0 ? accessSequence : accessSequence.Substring(0, dot);
        }

        /// <summary>Reads every member of the given blocks.</summary>
        public List<SnapshotEntry> ReadBlocks(IEnumerable<PlcBlock> blocks, IProgress<(int done, int total)> progress)
        {
            if (_varsByAccessKey == null) throw new InvalidOperationException("먼저 DB 목록을 읽어야 합니다.");
            var vars = new List<VarInfo>();
            foreach (var b in blocks)
            {
                if (_varsByAccessKey.TryGetValue(b.AccessKey, out var list)) vars.AddRange(list);
            }
            return ReadVars(vars, progress);
        }

        /// <summary>Reads the given tags by name (used for "compare with current PLC values").</summary>
        public List<SnapshotEntry> ReadTagsByName(IEnumerable<string> names, IProgress<(int done, int total)> progress)
        {
            if (_varsByName == null) throw new InvalidOperationException("먼저 DB 목록을 읽어야 합니다.");
            // Tags that no longer exist on the PLC are left out, so the comparer reports them as "only in A".
            var vars = new List<VarInfo>();
            foreach (var n in names)
            {
                if (_varsByName.TryGetValue(n, out var v)) vars.Add(v);
            }
            return ReadVars(vars, progress);
        }

        private List<SnapshotEntry> ReadVars(List<VarInfo> vars, IProgress<(int done, int total)> progress)
        {
            var result = new List<SnapshotEntry>(vars.Count);
            var consecutiveFailures = 0;
            for (var start = 0; start < vars.Count; start += ReadChunk)
            {
                var chunk = vars.Skip(start).Take(ReadChunk).ToList();
                var tags = new List<PlcTag>(chunk.Count);
                var tagVar = new List<VarInfo>(chunk.Count);
                foreach (var v in chunk)
                {
                    PlcTag tag = null;
                    try { tag = PlcTags.TagFactory(v.Name, new ItemAddress(v.AccessSequence), v.Softdatatype); }
                    catch { /* fall through */ }
                    if (tag == null)
                    {
                        result.Add(new SnapshotEntry(v.Name, TypeName(v.Softdatatype), "", "지원 안 되는 형식"));
                        continue;
                    }
                    tags.Add(tag);
                    tagVar.Add(v);
                }

                if (tags.Count > 0)
                {
                    var res = Conn.ReadTags(tags);
                    if (res != 0)
                    {
                        consecutiveFailures++;
                        foreach (var v in tagVar) result.Add(new SnapshotEntry(v.Name, TypeName(v.Softdatatype), "", "읽기 실패 " + res));
                        if (consecutiveFailures >= 3)
                        {
                            throw new InvalidOperationException("PLC 읽기가 연속으로 실패했습니다 (오류 코드 " + res + "). 연결 상태를 확인하세요.");
                        }
                    }
                    else
                    {
                        consecutiveFailures = 0;
                        for (var i = 0; i < tags.Count; i++)
                        {
                            var t = tags[i];
                            var ok = t.Quality == PlcTagQC.TAG_QUALITY_GOOD;
                            result.Add(new SnapshotEntry(
                                tagVar[i].Name,
                                TypeName(tagVar[i].Softdatatype),
                                ok ? ValueText(t) : "",
                                ok ? SnapshotEntry.StatusOk : "품질 불량 (오류 " + t.LastReadError + ")"));
                        }
                    }
                }
                progress?.Report((Math.Min(start + chunk.Count, vars.Count), vars.Count));
            }
            return result;
        }

        /// <summary>The driver formats values as "QQ: value" (QQ = quality); keep only the value, on one line.</summary>
        private static string ValueText(PlcTag tag)
        {
            var s = tag.ToString() ?? "";
            var sep = s.IndexOf(": ", StringComparison.Ordinal);
            if (sep >= 0 && sep <= 4) s = s.Substring(sep + 2);
            return s.Replace("\r", "\\r").Replace("\n", "\\n");
        }

        private static string TypeName(uint softdatatype)
        {
            try { return Softdatatype.Types[softdatatype]; }
            catch { return "0x" + softdatatype.ToString("X", CultureInfo.InvariantCulture); }
        }
    }
}

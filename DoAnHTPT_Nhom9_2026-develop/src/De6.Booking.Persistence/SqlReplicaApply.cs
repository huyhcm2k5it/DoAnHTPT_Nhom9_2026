using De6.Contracts;
using System.Security.Cryptography;
using System.Text.Json;
namespace De6.Booking.Persistence;
public sealed partial class SqlBookingStore
{
    public async Task<AppliedReceipt> AppendAndApplyAsync(PersistedEvent entry,AuthenticatedPeerAuthority peer,CancellationToken cancellation=default)
    {
        cancellation.ThrowIfCancellationRequested();
        entry=CopyEntry(entry);
        var value=DecodeEntry(entry);
        using var lease=gate.EnterAppend(peer);
        return await Transaction(async (connection,transaction,node)=>
        {
            Require(value.DatasetId==options.DatasetId,StoreRejection.DatasetMismatch);
            Require(peer.SenderNodeId is >=1 and <=3 && peer.SenderNodeId!=options.NodeId &&
                peer.SenderNodeId==peer.Authority.LeaderId && peer.Authority.LeaderId==node.Leader &&
                peer.Authority.Epoch==node.Epoch && entry.WriterId==peer.SenderNodeId && entry.Epoch==node.Epoch,
                StoreRejection.StaleAuthority);
            var duplicate=await VerifyPosition(connection,transaction,node,entry,value);
            var receipt=new AppliedReceipt(options.NodeId,peer.Authority.Epoch,entry.Epoch,entry.LogIndex,entry.EventId,entry.EventHash.ToArray(),null);
            if(duplicate) return (receipt,(PersistedEvent?)null);
            var state=await LoadState(connection,transaction,value.CommandData);
            ValidateDecision(value,state);
            await Apply(connection,transaction,node,value,entry,state);
            return (receipt,(PersistedEvent?)entry);
        },value.RequestId,cancellation);
    }
    public async Task<AppliedReceipt> ApplyReplayEntryAsync(PersistedEvent entry,RecoveryContext context,CancellationToken cancellation=default)
    {
        cancellation.ThrowIfCancellationRequested();
        entry=CopyEntry(entry);
        var value=DecodeEntry(entry);
        using var lease=gate.EnterReplay(context);
        return await Transaction(async(connection,transaction,node)=>
        {
            Require(context.RunId!=Guid.Empty && context.DatasetId==options.DatasetId && value.DatasetId==options.DatasetId,
                StoreRejection.DatasetMismatch);
            Require(context.TargetNodeId==options.NodeId && context.SourceNodeId is >=1 and <=3 && context.SourceNodeId!=options.NodeId &&
                context.TargetLeaderId is >=1 and <=3 && context.TargetEpoch>0 && context.TargetEpoch>=node.Epoch && entry.Epoch<=context.TargetEpoch,
                StoreRejection.StaleAuthority);
            Require(context.BaseIndex>=0 && context.TargetIndex>=context.BaseIndex && node.Index>=context.BaseIndex && node.Index<=context.TargetIndex &&
                entry.LogIndex>context.BaseIndex && entry.LogIndex<=context.TargetIndex && context.BaseHash.Length==32 && context.HeadHash.Length==32,
                StoreRejection.IndexMismatch);
            byte[] baseHash;
            if(context.BaseIndex==0)
                baseHash=SHA256.HashData(System.Text.Encoding.ASCII.GetBytes("PTIT-DE6-V12-SEED|"+options.DatasetId.ToString("N")));
            else
            {
                var baseline=await FindEvent(connection,transaction,"l.LogIndex=@id",context.BaseIndex);
                Require(baseline!=null,StoreRejection.PrefixConflict);
                baseHash=baseline.EventHash.ToArray();
            }
            Require(CryptographicOperations.FixedTimeEquals(baseHash,context.BaseHash.Span),StoreRejection.PrefixConflict);
            if(node.Index==context.BaseIndex) Require(CryptographicOperations.FixedTimeEquals(node.Hash,baseHash),StoreRejection.PrefixConflict);
            if(node.Index==context.TargetIndex) Require(CryptographicOperations.FixedTimeEquals(node.Hash,context.HeadHash.Span),StoreRejection.PrefixConflict);
            if(entry.LogIndex==context.TargetIndex) Require(CryptographicOperations.FixedTimeEquals(entry.EventHash.Span,context.HeadHash.Span),StoreRejection.PrefixConflict);
            var duplicate=await VerifyPosition(connection,transaction,node,entry,value);
            var receipt=new AppliedReceipt(options.NodeId,context.TargetEpoch,entry.Epoch,entry.LogIndex,entry.EventId,entry.EventHash.ToArray(),context.RunId);
            if(duplicate) return (receipt,(PersistedEvent?)null);
            var state=await LoadState(connection,transaction,value.CommandData);
            ValidateDecision(value,state);
            await Apply(connection,transaction,node,value,entry,state);
            return (receipt,(PersistedEvent?)entry);
        },value.RequestId,cancellation);
    }

    private static PersistedEvent CopyEntry(PersistedEvent entry)
    {
        if(entry.PayloadBytes.Length is 0 or >1024*1024 || entry.EventHash.Length!=32 || entry.ResultPayload.Length>1024*1024)
            throw new StoreRejectedException(StoreRejection.InvalidEvent);
        return entry with { PayloadBytes=entry.PayloadBytes.ToArray(),EventHash=entry.EventHash.ToArray(),ResultPayload=entry.ResultPayload.ToArray() };
    }
    private static BookingEvent DecodeEntry(PersistedEvent entry)
    {
        try { return BookingEventCodec.Decode(entry); }
        catch(Exception e) when(e is ArgumentException or JsonException or FormatException)
        { throw new StoreRejectedException(StoreRejection.InvalidEvent); }
    }
    private async Task<bool> VerifyPosition(Microsoft.Data.SqlClient.SqlConnection connection,Microsoft.Data.SqlClient.SqlTransaction transaction,
        Node node,PersistedEvent entry,BookingEvent value)
    {
        if(entry.LogIndex<=node.Index)
        {
            var existing=await FindEvent(connection,transaction,"l.LogIndex=@id",entry.LogIndex);
            Require(existing!=null && existing.EventId==entry.EventId && existing.Epoch==entry.Epoch &&
                existing.WriterId==entry.WriterId && existing.PayloadBytes.Span.SequenceEqual(entry.PayloadBytes.Span) &&
                CryptographicOperations.FixedTimeEquals(existing.EventHash.Span,entry.EventHash.Span) &&
                existing.ResultPayload.Span.SequenceEqual(entry.ResultPayload.Span),StoreRejection.PrefixConflict);
            return true;
        }
        Require(entry.LogIndex==node.Index+1,StoreRejection.Gap,node.Index+1);
        Require(value.PreviousHash==Convert.ToHexStringLower(node.Hash),StoreRejection.PrefixConflict);
        Require(await FindEvent(connection,transaction,"l.RequestId=@id",value.RequestId)==null,StoreRejection.InvariantViolation);
        return false;
    }
}

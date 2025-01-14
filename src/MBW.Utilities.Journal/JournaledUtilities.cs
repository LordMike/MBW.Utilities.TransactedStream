﻿using MBW.Utilities.Journal.Exceptions;
using MBW.Utilities.Journal.Helpers;
using MBW.Utilities.Journal.SparseJournal;
using MBW.Utilities.Journal.Structures;
using MBW.Utilities.Journal.WalJournal;

namespace MBW.Utilities.Journal;

public static class JournaledUtilities
{
    /// <summary>
    /// If a journal exists for this stream, and it was committed but not yet applied, this function will apply it. If the journal exists, but wasn't committed, it is discarded.
    /// </summary>
    public static void EnsureJournalCommitted(Stream origin, string journalFile)
    {
        EnsureJournalCommitted(origin, new FileBasedJournalStreamFactory(journalFile));
    }

    /// <summary>
    /// If a journal exists for this stream, and it was committed but not yet applied, this function will apply it. If the journal exists, but wasn't committed, it is discarded.
    /// </summary>
    public static void EnsureJournalCommitted(Stream origin, IJournalStreamFactory journalStreamFactory)
    {
        if (!journalStreamFactory.Exists(string.Empty))
            return;

        // If this is completed, commit it, else delete it
        using Stream fsJournal = journalStreamFactory.OpenOrCreate(string.Empty);

        if (!JournaledStreamHelpers.TryRead(fsJournal, JournalFileConstants.HeaderMagic, out JournalFileHeader header))
        {
            // Corrupt file. The file exists, but does not have a valid header. This is unlike if the footer is missing (a partially written file)
            throw new JournalCorruptedException("The journal file was corrupted", false);
        }

        if (header.Strategy == JournalStrategy.WalJournalFile)
        {
            fsJournal.Seek(-WalJournalFooter.StructSize, SeekOrigin.End);
            if (!JournaledStreamHelpers.TryRead(fsJournal, WalJournalFileConstants.WalJournalFooterMagic, out WalJournalFooter footer))
            {
                // Bad file or not committed
                journalStreamFactory.Delete(string.Empty);
                return;
            }

            if (header.Nonce != footer.HeaderNonce)
                throw new JournalCorruptedException($"Header & footer does not match. Nonces: {header.Nonce:X8}, footer: {footer.HeaderNonce:X8}", false);

            fsJournal.Seek(0, SeekOrigin.Begin);
            WalFileJournalHelpers.ApplyJournal(origin, fsJournal);

            // Applied
            journalStreamFactory.Delete(string.Empty);
        }
        else if (header.Strategy == JournalStrategy.SparseFile)
        {
            fsJournal.Seek(-SparseJournalFooter.StructSize, SeekOrigin.End);
            if (!JournaledStreamHelpers.TryRead(fsJournal, SparseJournalFileConstants.SparseJournalFooterMagic, out SparseJournalFooter footer))
            {
                // Bad file or not committed
                journalStreamFactory.Delete(string.Empty);
                return;
            }

            if (header.Nonce != footer.HeaderNonce)
                throw new JournalCorruptedException($"Header & footer does not match. Nonces: {header.Nonce:X8}, footer: {footer.HeaderNonce:X8}", false);

            fsJournal.Seek(0, SeekOrigin.Begin);
            SparseJournalHelpers.ApplyJournal(origin, fsJournal, header, footer);

            // Applied
            journalStreamFactory.Delete(string.Empty);
        }
        else
        {
            throw new InvalidOperationException("Unknown journal type: " + header.Strategy);
        }
    }
}
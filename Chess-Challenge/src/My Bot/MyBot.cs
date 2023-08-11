﻿using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Numerics;
using System.Security.Cryptography;
using ChessChallenge.API;
using ChessChallenge.Chess;
using Microsoft.CodeAnalysis;
using static ChessChallenge.Application.ConsoleHelper;
using Board = ChessChallenge.API.Board;
using Move = ChessChallenge.API.Move;

public class MyBot : IChessBot
{
    Board board;
    Timer timer;
    private Move bestMove;
    int[] pieceValues = { 0, 100, 300, 300, 500, 900, 10000 }; // TODO which values?
    int maxExpectedMoveDuration;

    public Move Think(Board _board, Timer _timer)
    {
        board = _board;
        timer = _timer;
        bestMove = Move.NullMove;
        maxExpectedMoveDuration = 10000000;

        // Time control
        var depth = 8;
        // Add 2, as king has a lot of movements and king always is on the board
        var pieceCountSquare = (BitboardHelper.GetNumberOfSetBits(board.WhitePiecesBitboard) + 2) * (BitboardHelper.GetNumberOfSetBits(board.BlackPiecesBitboard) + 2);
        while (maxExpectedMoveDuration > timer.MillisecondsRemaining / 10 - 200 && depth > 3)
        {
            depth--;
            maxExpectedMoveDuration = (int) (Math.Pow(pieceCountSquare, (depth - 2) / 1.5) / 10);
        }
        
        // Search
        minimax(depth, board.IsWhiteToMove, -1000000000.0, 1000000000.0, true);
        //Console.WriteLine($"pieces={pieceCountSquare}, depth={depth}, expected={maxExpectedMoveDuration}, actual={timer.MillisecondsElapsedThisTurn},\tovershoot={Math.Max(0, timer.MillisecondsElapsedThisTurn - maxExpectedMoveDuration)}");

        return bestMove;
    }

    bool isHighPotentialMove(Move move)
    {
        board.MakeMove(move);
        var isInCheck = board.IsInCheck();
        board.UndoMove(move);
        return move.IsCapture || move.IsPromotion || move.IsCastles || isInCheck;
    }
    
    double minimax(int depth, bool whiteToMinimize, double alpha, double beta, bool assignBestMove)
    {
        var moves = board.GetLegalMoves(); // TODO: non-alloc
        
        if (depth == 0 || board.IsInCheckmate() || board.IsDraw() || (moves.Length == 1 && assignBestMove)) // TODO 3 cases different?
        {
            if (assignBestMove) bestMove = moves.First();
            return evaluate(); // Reminder: Don't cache if moves.Length == 1 && assignBestMove, this is just a shortcut
        }
            
        // Optimize ab-pruning: first check moves that are more likely to be good
        moves = moves.Where(move => isHighPotentialMove(move))
            .Concat(moves.Where(move => !isHighPotentialMove(move)))
            .ToArray();

        if (whiteToMinimize)
        {
            var maxEval = Double.NegativeInfinity; // TODO extract function for both cases to spare code?
            foreach (var move in moves)
            {
                board.MakeMove(move);
                var eval = minimax(depth - 1, false, alpha, beta, false);
                board.UndoMove(move);
                alpha = Math.Max(alpha, eval);
                if (eval > maxEval)
                {
                    maxEval = eval;
                    if (assignBestMove) bestMove = move;
                }

                if (beta <= alpha) break;
            }

            return maxEval;
        }
        else
        {
            var minEval = Double.PositiveInfinity;
            foreach (var move in moves)
            {
                board.MakeMove(move);
                var eval = minimax(depth - 1, true, alpha, beta, false);
                board.UndoMove(move);
                beta = Math.Min(beta, eval);
                if (eval < minEval)
                {
                    minEval = eval;
                    if (assignBestMove) bestMove = move;
                }

                if (beta <= alpha) break;
            }

            return minEval;
        }
    }

    double evaluate()
    {
        return evaluate(true) - evaluate(false); // TODO strategy-evaluate (e.g. divide/multiply by how many plys played)
    }

    double evaluate(bool white)
    {
        // Checkmate is of course always best
        if (board.IsInCheckmate())
        {
            return board.IsWhiteToMove == white ? -100000000.0 : 100000000.0;
        }

        if (board.IsDraw())
        {
            return 0;
        }

        var score = 0.0;

        foreach (var pieceList in board.GetAllPieceLists())
        {
            if (white != pieceList.IsWhitePieceList) continue;
            for (int pieceIndex = 0; pieceIndex < pieceList.Count; pieceIndex++)
            {
                var piece = pieceList[pieceIndex];
                score += pieceValues[(int)piece.PieceType];

                if (piece.IsPawn)
                {
                    // Make pawns move forward
                    var rank = piece.Square.Rank;
                    var ranksAwayFromPromotion = white ? rank : 7 - rank;
                    score += ranksAwayFromPromotion;
                } // TODO endgame evaluation: king in center vs side/top/bottom (or near other pieces, no matter of color): board weight + 1 center-weight

                var attacks =
                    BitboardHelper.GetPieceAttacks(piece.PieceType, piece.Square, board, pieceList.IsWhitePieceList);

                // Move pieces to places with much freedom TODO up to how much freedom is it relevant? bishop < 2 freedom = trapped = very bad
                // TODO freedom is more important, should lead to moving pawn forward after castling
                // TODO weight bei how "relevant" is attacking/protecting piece
                //score += 2 * BitboardHelper.GetNumberOfSetBits(attacks);

                // TODO Make pieces protect other pieces 
                // TODO Pinning

                // Make pieces attacking/defending other pieces TODO same score for attack+defense?
                score += 1.5 * BitboardHelper.GetNumberOfSetBits(attacks & board.AllPiecesBitboard);
            }
        }

        // TODO favour early castle & castle rights

        // Putting someone in check is quite often good
        if (board.IsInCheck())
        {
            score += board.IsWhiteToMove == white ? 70 : -70;
        }

        return score;
    }
}
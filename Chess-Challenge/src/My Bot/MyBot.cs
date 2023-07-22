using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using ChessChallenge.API;

public class MyBot : IChessBot {
	public Move Think(Board board, Timer timer) {
		Move[] moves = board.GetLegalMoves();
		List<Move> best = new List<Move>(moves.Length);
		double bestEval = double.MinValue;
		foreach (Move move in moves) {
			double eval = EvalMove(board, move);
			if (eval > bestEval) {
				best.Clear();
				best.Add(move);
				bestEval = eval;
			} else if (eval == bestEval) {
				best.Add(move);
			}
		}

		Console.WriteLine(bestEval);

		return best[RandomNumberGenerator.GetInt32(best.Count)];
	}

	private static double EvalMove(Board board, Move move) {
		double eval = 0;
		if (move.IsCapture) {
			return EvalCapture(board, move);
		}

		board.MakeMove(move);
		foreach (PieceList pieceList in board.GetAllPieceLists()) {
			double pieceValue = PieceValue(pieceList.TypeOfPieceInList);
			bool notSameColor = pieceList.IsWhitePieceList ^ board.IsWhiteToMove;
			bool skipSuccessful = false;
			if (notSameColor) {
				pieceValue = -pieceValue;
				skipSuccessful = board.TrySkipTurn();
			}

			if (notSameColor == skipSuccessful) {
				eval += pieceValue * pieceList.Count(piece => board.SquareIsAttackedByOpponent(piece.Square));
				if (skipSuccessful) {
					board.UndoSkipTurn();
				}
			}
		}

		board.UndoMove(move);

		return eval / 32;
	}

	private static double EvalCapture(Board board, Move move) {
		double eval = PieceValue(move.CapturePieceType);
		if (board.SquareIsAttackedByOpponent(move.TargetSquare)) {
			eval -= PieceValue(move.MovePieceType);
		}

		return eval;
	}

	private static double PieceValue(PieceType piece) {
		switch (piece) {
			case PieceType.Pawn: {
				return 1;
			}
			case PieceType.Bishop:
			case PieceType.Knight: {
				return 3;
			}
			case PieceType.Rook: {
				return 5;
			}
			case PieceType.Queen: {
				return 9;
			}
			default: {
				return double.MaxValue;
			}
		}
	}
}

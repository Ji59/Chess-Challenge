using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Security.Cryptography;
using ChessChallenge.API;

public class MyBotRL : IChessBot {
	static Dictionary<ulong, Evaluation> Boards = new();
	static Dictionary<int, int> MoveCount = new();


	public Move Think(Board board, Timer timer) {
		double[][][] bitboard = GetBitBoard(board);

		Boards.TryAdd(board.ZobristKey, new());
		Evaluation startingEvaluation = Boards[board.ZobristKey];

		Move[] moves = board.GetLegalMoves();
		List<MoveEvaluation> movesEval = new();
		foreach (Move move in moves) {
			board.MakeMove(move);
			if (!Boards.ContainsKey(board.ZobristKey)) {
				startingEvaluation.Update(1 - Expand(board, move.IsCapture));
			}

			movesEval.Add(new MoveEvaluation(move, Boards[board.ZobristKey]));
			board.UndoMove(move);
		}

		double stopAfter = 0.95 * timer.MillisecondsRemaining;
		while (timer.MillisecondsRemaining > stopAfter) {
			MoveEvaluation? move = null;
			bool normalEval = true;
			double selectedVal = double.MinValue;
			foreach (MoveEvaluation moveEvaluation in movesEval) {
				normalEval = UpdateEval(moveEvaluation, Boards[board.ZobristKey].Visits, normalEval, ref selectedVal, ref move);
			}

			board.MakeMove(move.move);
			double result = Select(board);
			move.Update(result);
			board.UndoMove(move.move);
			startingEvaluation.Update(1 - result);
		}


		foreach (KeyValuePair<int, int> movesCount in MoveCount.ToImmutableSortedDictionary()) {
			Console.Write(movesCount.Key + " " + movesCount.Value + " \t");
		}

		Console.WriteLine();
		MoveCount.Clear();

		Move? best = null;
		double bestValue = double.MinValue;
		double totalValue = 0;
		// double totalVisits = 0;
		foreach (MoveEvaluation move in movesEval) {
			double value = 1 - move.Average();
			// totalVisits += move.Visits;
			if (value > bestValue) {
				// Console.WriteLine(value + ": " + move.Visits);
				best = move.move;
				bestValue = value;
			}

			if (move.move.IsCapture) {
				Console.WriteLine(move.move + "\t" + value);
			}
		}

		// Console.WriteLine(zobristKey + "; " + board.GetFenString());
		Console.WriteLine((board.IsWhiteToMove ? "W" : "B") + "\t" + bestValue + "; " + startingEvaluation.Visits);

		return best.Value;
	}

	private PieceType[] pieceTypes = {
		PieceType.Pawn,
		PieceType.Knight,
		PieceType.Bishop,
		PieceType.Rook,
		PieceType.Queen,
		PieceType.King
	};

	private double[][][] GetBitBoard(Board board) {
		double[][][] bitboards = new double[8][][];
		for (int i = 0; i < 8; i++) {
			bitboards[i] = new double[8][];
			for (int j = 0; j < 8; j++) {
				bitboards[i][j] = new double[2 * pieceTypes.Length];
			}
		}

		for (int i = 0; i < pieceTypes.Length; i++) {
			PieceType pieceType = pieceTypes[i];
			ulong bitboard = board.GetPieceBitboard(pieceType, true);
			int[] vals = new int[64];
			for (int b = 0; b < 64; b++) {
				if ((bitboard & (1UL << b)) > 0) {
					bitboards[b / 8][b % 8][i] = 1D;
					vals[b] = 1;
				} else {
					vals[b] = 0;
				}
			}

			bitboard = board.GetPieceBitboard(pieceType, false);
			vals = new int[64];
			for (int b = 0; b < 64; b++) {
				if ((bitboard & (1UL << b)) > 0) {
					bitboards[b / 8][b % 8][i + pieceTypes.Length] = 1D;
					vals[b] = 1;
				} else {
					vals[b] = 0;
				}
			}

			int x = 0;
		}


		return bitboards;
	}

	double[][][] convolute(int filters, double[][][] input, double[][][][] weigts, double bias) {
		int kernelSize = weigts.Length;
		int size = input.Length - kernelSize + 1;
		int inFilters = input[0][0].Length;
		double[][][] result = new double[size][][];

		for (int i = 0; i < size; i++) {
			result[i] = new double[size][];
			for (int j = 0; j < size; j++) {
				result[i][j] = new double[filters];
				for (int x = 0; x < kernelSize; x++) {
					for (int y = 0; y < kernelSize; y++) {
						for (int e = 0; e < inFilters; e++) {
							double val = input[i + x][j + y][e];
							for (int f = 0; f < filters; f++) {
								result[i][j][f] += val * weigts[x][y][e][f];
							}
						}
					}
				}
				// Apply RELU activation
				for (int f = 0; f < filters; f++) {
					result[i][j][f] = Math.Max(result[i][j][f] + bias, 0);
				}
			}
		}

		return result;
	}

	private static bool UpdateEval(
		MoveEvaluation moveEvaluation, int boardVisits, bool normalEval,
		ref double selectedVal,
		ref MoveEvaluation? move
	) {
		var (avg, actionVal) = ActionValue(moveEvaluation, boardVisits);

		if (avg > 0.75) {
			if (normalEval || avg > selectedVal) {
				selectedVal = avg;
				move = moveEvaluation;
			}

			normalEval = false;
		} else if (normalEval && actionVal > selectedVal) {
			move = moveEvaluation;
			selectedVal = actionVal;
		}

		return normalEval;
	}

	private double Select(Board board) {
		int i = 1;
		Stack<Move> doneMoves = new();
		for (
			ulong zobristKey = board.ZobristKey;
			Boards.ContainsKey(zobristKey) && !(board.IsDraw() || board.IsInCheckmate());
			zobristKey = board.ZobristKey, i++
		) {
			// Console.WriteLine(zobristKey + "; " + board.GetFenString());
			int boardVisits = Boards[zobristKey].Visits;
			MoveEvaluation? move = null;
			double selectedVal = double.MinValue;
			bool normalEval = true;
			foreach (Move moveCandidate in board.GetLegalMoves()) {
				if (!(moveCandidate.PromotionPieceType == PieceType.Queen ||
				      moveCandidate.PromotionPieceType == PieceType.None)) {
					continue;
				}

				board.MakeMove(moveCandidate);
				zobristKey = board.ZobristKey;
				if (!Boards.ContainsKey(zobristKey)) {
					doneMoves.Push(moveCandidate);
					move = null;
					break;
				}

				normalEval = UpdateEval(
					new(moveCandidate, Boards[zobristKey]),
					boardVisits, normalEval, ref selectedVal, ref move
				);

				board.UndoMove(moveCandidate);
				if (!normalEval && selectedVal > 0.984375) {
					// 1 - 2^-6
					break;
				}
			}

			// Move move = moves[RandomNumberGenerator.GetInt32(moves.Length)];

			if (move != null) {
				Move moveVal = move.move;
				doneMoves.Push(moveVal);
				board.MakeMove(moveVal);
			}
		}

		MoveCount.TryAdd(i, 0);
		MoveCount[i]++;

		double result = 0.5;
		if (board.IsInCheckmate()) {
			result = 0;
			Boards.TryAdd(board.ZobristKey, new(0, 1));
			// Console.WriteLine((board.IsWhiteToMove ? "W" : "B") + "\tcheckmate");
		} else if (board.IsDraw()) {
			Boards.TryAdd(board.ZobristKey, new(0.5, 1));
			// Console.WriteLine((board.IsWhiteToMove ? "W" : "B") + "\tdraw");
		} else {
			result = Expand(board, doneMoves.Peek().IsCapture);
			// Console.WriteLine((board.IsWhiteToMove ? "W" : "B") + "\texpand");
		}

		foreach (Move move in doneMoves) {
			board.UndoMove(move);
			result = 1 - result;
			// Console.WriteLine(board.ZobristKey + "; " + board.GetFenString());
			Boards[board.ZobristKey].Update(result);
		}

		return result;
	}

	private double Expand(Board board, bool capture = false) {
		bool whiteToMove = board.IsWhiteToMove;
		Stack<Move> doneMoves = new();
		for (int i = 0; i < 32 && !(board.IsDraw() || board.IsInCheckmate()); i++) {
			Move[] moves = capture && board.GetLegalMoves(true).Length > 0 ?
				board.GetLegalMoves(true) :
				board.GetLegalMoves();
			Move move = moves[RandomNumberGenerator.GetInt32(moves.Length)];
			capture = move.IsCapture;
			doneMoves.Push(move);
			board.MakeMove(move);
		}

		double result = board.IsInCheckmate() ? (whiteToMove.Equals(board.IsWhiteToMove) ? 0 : 1)
			: board.IsDraw() && !board.IsRepeatedPosition() ? 0.5
			: EvalMove(board);
		// Console.WriteLine((board.IsWhiteToMove ? "W" : "B") + "\t" + (board.IsInCheckmate()
		// ? "C" : (board.IsDraw() ? "D" : "E")) + "\t" + result);
		foreach (Move move in doneMoves) {
			board.UndoMove(move);
		}

		Boards.Add(board.ZobristKey, new(result));
		return result;
	}

	private static (double, double) ActionValue(Evaluation evaluation, int boardVisits) {
		double average = 1 - evaluation.Average();
		return (average, average + Math.Sqrt(0.5 * Math.Log2(boardVisits) / evaluation.Visits));
	}

	private static double EvalMove(Board board) {
		double eval = 0.5;

		foreach (PieceList pieceList in board.GetAllPieceLists()) {
			double pieceValue = PieceValue(pieceList.TypeOfPieceInList) / 24;
			if (pieceList.IsWhitePieceList ^ board.IsWhiteToMove) {
				pieceValue = -pieceValue;
			}

			double value = pieceValue * pieceList.Count;
			eval += value;
			// Console.WriteLine((pieceList.IsWhitePieceList ? "W" : "B") + "\t" + pieceList.TypeOfPieceInList + "\t" + value);
		}

		// Console.WriteLine((board.IsWhiteToMove ? "W\t" : "B\t") + eval);
		return Math.Min(Math.Max(eval, 0.015625), 0.984375);
	}

	private static double PieceValue(PieceType piece) {
		switch (piece) {
			case PieceType.Pawn:
				return 1;
			case PieceType.Bishop:
			case PieceType.Knight:
				return 3;
			case PieceType.Rook:
				return 5;
			// case PieceType.Queen:
			// return 16;
		}

		return 9;
	}

	protected class Evaluation {
		public double TotalValue { get; set; }
		public int Visits { get; set; }

		public Evaluation() {
			TotalValue = 0;
			Visits = 0;
		}

		public Evaluation(double value) {
			TotalValue = value;
			Visits = 1;
		}

		public Evaluation(double value, int visits) {
			TotalValue = value;
			this.Visits = visits;
		}

		protected Evaluation(Evaluation evaluation) {
			TotalValue = evaluation.TotalValue;
			Visits = evaluation.Visits;
		}

		public void Update(double score) {
			TotalValue += score;
			Visits++;
		}

		public double Average() {
			return TotalValue / Visits;
		}
	}

	private class MoveEvaluation : Evaluation {
		public readonly Move move;

		// public MoveEvaluation(Move move) : base() {
		// 	this.move = move;
		// }

		// public MoveEvaluation(Move move, double value) : base(value) {
		// 	this.move = move;
		// }

		public MoveEvaluation(Move move, Evaluation evaluation) : base(evaluation) {
			this.move = move;
		}
	}
}

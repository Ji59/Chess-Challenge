using System;
using ChessChallenge.API;
using Stockfish.NET;

public class StockfishBot : IChessBot
{
	const int STOCKFISH_LEVEL = 0;

	IStockfish mStockFish;

	public StockfishBot()
	{
		Stockfish.NET.Models.Settings stockfishSettings = new Stockfish.NET.Models.Settings();
		stockfishSettings.SkillLevel = STOCKFISH_LEVEL;

		mStockFish = new Stockfish.NET.Stockfish(@"C:\Program Files\stockfish-windows-x86-64-modern\stockfish_20090216_x64_ssse.exe", 2, stockfishSettings);
	}

	public Move Think(Board board, Timer timer)
	{
		string fen = board.GetFenString();
		mStockFish.SetFenPosition(fen);

		string bestMove = mStockFish.GetBestMoveTime(GetTime(board,timer));

		return new Move(bestMove, board);
	}

	// Basic time management
	public int GetTime(Board board, Timer timer)
	{
		return Math.Min(board.PlyCount * 150 + 100, timer.MillisecondsRemaining / 20);
	}
}
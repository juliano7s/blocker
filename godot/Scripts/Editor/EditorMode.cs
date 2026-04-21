namespace Blocker.Game.Editor;

public enum EditorMode
{
    Paint,   // left-click paints selected tile; right-click erases
    Fill,    // flood-fill from clicked cell with selected tile
    Pick,    // read tile under cursor → set tool, switch back to Paint
    Select,  // drag to select rectangle; drag inside to move; Delete to clear
    Line,    // click start + drag → Bresenham preview → release to commit
    Erase    // always erases on left-click
}

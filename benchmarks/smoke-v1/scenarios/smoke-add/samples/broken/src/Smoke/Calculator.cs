namespace Smoke;

public static class Calculator
{
    public static int Add(int left, int right) => unchecked(left - right);
}

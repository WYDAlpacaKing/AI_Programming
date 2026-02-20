using System.Linq; // 必须引入 Linq 才能使用 Any 和 FirstOrDefault
using UnityEngine;
using AlanZucconi.AI.BT;
using AlanZucconi.Snake;

namespace Snake.YandongWang33899181
{
    [CreateAssetMenu(fileName = "SnakeAI_YandongWang33899181", menuName = "Snake/2024-25/SnakeAI_YandongWang33899181")]
    public class SnakeAI_YandongWang33899181 : SnakeAI
    {
        public override Node CreateBehaviourTree(SnakeGame Snake)
        {
            // Selector: 从上到下依次尝试，成功一个就停止。体现了“优先级”的概念。
            return new Selector
            (
                // 优先级 1: 【激进模式】如果食物可以到达，直接用寻路去吃食物
                new Filter
                (
                    Snake.IsFoodReachable,
                    new Action(Snake.MoveTowardsFood)
                ),

                // 优先级 2: 【拖延模式】食物被墙或者身体挡住了，不可达。
                // 策略：去找自己的尾巴！因为尾巴会不断往前移动，跟着尾巴走永远是最安全的“拖延时间”战术。
                new Filter
                (
                    () => Snake
                        .AvailableNeighbours(Snake.TailPosition)
                        .Any(position => Snake.IsReachable(position)), // 检查尾巴周围有没有可达的空地
                    new Action
                    (
                        () => Snake.MoveTowards(
                            Snake.AvailableNeighbours(Snake.TailPosition)
                            .FirstOrDefault(position => Snake.IsReachable(position))
                        )
                    )
                ),

                // 优先级 3: 【绝境求生模式】食物吃不到，尾巴也摸不到。
                // 策略：千万别撞墙或撞自己，随便找个空地走一步算一步，祈祷空间能让出来。
                // 注意：这里用局部坐标系 (Local) 判断更符合直觉
                new Selector
                (
                    // 如果正前方是空的，什么都不做（保持前进）
                    new Filter(Snake.IsFreeAhead, Action.Nothing),
                    // 前面堵死了，看看左边
                    new Filter(Snake.IsFreeLeft, new Action(Snake.TurnLeft)),
                    // 左右只能选右边了
                    new Filter(Snake.IsFreeRight, new Action(Snake.TurnRight))
                )
            );
        }
    }
}


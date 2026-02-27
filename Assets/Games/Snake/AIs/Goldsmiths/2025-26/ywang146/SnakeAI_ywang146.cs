using AlanZucconi.AI.BT;
using AlanZucconi.AI.PF; 
using AlanZucconi.Snake;
using System.Collections.Generic;
using System.Linq; // 必须引入 Linq 才能使用 Any 和 FirstOrDefault
using UnityEngine;

namespace Snake.ywang146
{
    [CreateAssetMenu(fileName = "SnakeAI_ywang146", menuName = "Snake/2024-25/SnakeAI_ywang146")]
    public class SnakeAI_ywang146 : SnakeAI
    {
        // 我们需要一个自定义的类来适配贪吃蛇的地图，以便 PF 库能运行 A*
        // 它实现了 IPathfinding<Vector2Int> 接口，定义了“哪些格子可以走”
        private class SnakeGridGraph : IPathfinding<Vector2Int>
        {
            private SnakeGame _snake;
            public SnakeGridGraph(SnakeGame snake) { _snake = snake; }

            // PF 库的核心要求：给定一个节点，返回它可以去往的所有邻居
            public IEnumerable<Vector2Int> Outgoing(Vector2Int position)
            {
                // 直接使用贪吃蛇 API 获取周围可走的格子
                return _snake.AvailableNeighbours(position);
            }
        }

        public override Node CreateBehaviourTree(SnakeGame Snake)
        {
            // 实例化我们的适配器图
            SnakeGridGraph graph = new SnakeGridGraph(Snake);

            // 这是一个将 IPathfinding 转换为可计算成本的图的包装器，这样才能用 Dijkstra 和 A*
            UnitCostGraph<Vector2Int> costGraph = graph.ToWeightedGraph();

            return new Selector
            (
                // ==========================================
                // 优先级 1: 【A* 高级寻路吃食物】
                // ==========================================
                new Filter
                (
                    () =>
                    {
                        // 1. 判断食物是否可达 (使用 BFS 的快速判断足够了，只为了 true/false)
                        return Snake.IsFoodReachable();
                    },
                    new Action
                    (
                        () =>
                        {
                            // 2. 如果可达，我们不再使用默认的 MoveTowardsFood
                            // 我们使用 A* 计算出一条最佳路径
                            List<(Vector2Int node, Edge edge)> path = costGraph.AStar(
                                Snake.HeadPosition,
                                Snake.FoodPosition,
                                // 启发式函数 (Heuristic): 曼哈顿距离 (Manhattan Distance)，非常适合网格游戏
                                (a, b) => Mathf.Abs(a.x - b.x) + Mathf.Abs(a.y - b.y)
                            );

                            // 如果找到了路径 (path != null) 并且路径长度大于1 (包含起点本身)
                            if (path != null && path.Count > 1)
                            {
                                // path[0] 是现在的头，path[1] 是下一步要走的格子
                                Vector2Int nextStep = path[1].node;

                                // 将坐标差转化为方向指令
                                if (nextStep.y > Snake.HeadPosition.y) Snake.GoNorth();
                                else if (nextStep.y < Snake.HeadPosition.y) Snake.GoSouth();
                                else if (nextStep.x > Snake.HeadPosition.x) Snake.GoEast();
                                else if (nextStep.x < Snake.HeadPosition.x) Snake.GoWest();
                            }
                        }
                    )
                ),

                // ==========================================
                // 优先级 2: 【跟随尾巴保命】(沿用之前的策略，但更加稳健)
                // ==========================================
                new Filter
                (
                    () => Snake.AvailableNeighbours(Snake.TailPosition).Any(p => Snake.IsReachable(p)),
                    new Action
                    (
                        () =>
                        {
                            Vector2Int target = Snake.AvailableNeighbours(Snake.TailPosition)
                                                     .FirstOrDefault(p => Snake.IsReachable(p));

                            // 这里可以继续用默认的 MoveTowards，因为找尾巴通常不需要最优解，只要能动就行
                            Snake.MoveTowards(target);
                        }
                    )
                ),

                // ==========================================
                // 优先级 3: 【绝境盲目前进避障】(最低优先级)
                // ==========================================
                new Selector
                (
                    new Filter(Snake.IsFreeAhead, Action.Nothing),
                    new Filter(Snake.IsFreeLeft, new Action(Snake.TurnLeft)),
                    new Filter(Snake.IsFreeRight, new Action(Snake.TurnRight))
                )
            );
        }
    }
}


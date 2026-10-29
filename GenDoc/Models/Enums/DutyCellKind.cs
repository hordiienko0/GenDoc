namespace GenDoc.Models.Enums
{
    public enum DutyCellKind
    {
        /// <summary>«н» - людина в наряді.</summary>
        Duty = 1,

        /// <summary>Зелене - звільнений від наряду в цей день.</summary>
        Exempt = 2,

        /// <summary>«×» - не ставити в цей день (знятий авто-наряд); у друк не йде.</summary>
        Blocked = 3,

        /// <summary>Жовта «н» - наряд по столовій: лише вручну, у чергу нарядів не йде, але в цей день людину в наряд не ставлять.</summary>
        Canteen = 4
    }

    public enum DutyCellSource
    {
        /// <summary>Поставила програма; перераховується після кожної правки.</summary>
        Auto = 0,

        /// <summary>Поставлено вручну; програма не чіпає.</summary>
        Manual = 1,

        /// <summary>Звільнення з подій: відпустка, шпиталь, відрядження.</summary>
        Event = 2
    }
}

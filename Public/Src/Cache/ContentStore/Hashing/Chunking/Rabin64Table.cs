﻿// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using HashValueT = System.UInt64;

#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member
#pragma warning disable SA1121 // Use built-in type alias
#pragma warning disable SA1304 // Non-private readonly fields must begin with upper-case letter
#pragma warning disable SA1307 // Accessible fields must begin with upper-case letter
#pragma warning disable SA1310 // Field names must not contain underscore
#pragma warning disable SA1311 // Static readonly fields must begin with upper-case letter
#pragma warning disable SA1600 // Elements must be documented

namespace BuildXL.Cache.ContentStore.Hashing.Chunking
{
    //[GeneratedCode("Copied from Windows Sources", "1.0")]
    public static class Rabin64Table
    {
        internal static readonly HashValueT[] g_arrPolynomialsTD = new HashValueT[]
        {
    0,
    17489118014351221095,
    1719948946582692777,
    16531491954992890574,
    3439897893165385554,
    15927310780349212213,
    4062955623038493947,
    14616239836276229532,
    6879795786330771108,
    12523426432823832515,
    5235308833456778509,
    13407877486988872810,
    8125911246076987894,
    9399255506910931089,
    7429660988046781023,
    10785735598842907448,
    13759591572661542216,
    5496141480403221551,
    12190575508071481057,
    6600108791938113414,
    10470617666913557018,
    7204298594936290173,
    9696644991368149427,
    8369010900268194004,
    16251822492153975788,
    1387045323150504587,
    17749933008478335045,
    351766940112310562,
    14859321976093562046,
    4360397823355418073,
    15701966057237919511,
    3124727123976263280,
    10327338235959450615,
    9072439071613532816,
    10992282960806443102,
    7654354859834497337,
    11596465026119031973,
    5934406942433410498,
    13200217583876226828,
    5009484756929519211,
    15000332270614975827,
    2494491260117562420,
    14408597189872580346,
    3837149216610834333,
    18417218136473948673,
    946545909026747238,
    16738021800536388008,
    1944624892854735055,
    3576404333566035647,
    14056900910598399960,
    2774090646301009174,
    15333165267635881073,
    2169899539243450861,
    17053121943247118474,
    703533880224621124,
    18119846596928045859,
    7987135158109505563,
    11271899878477572476,
    8720795646710836146,
    10066575700027165397,
    4712165933309382473,
    12957188040766287406,
    6249454247952526560,
    11821757342575103367,
    17010644437389416073,
    2207932398209349614,
    18144878143227065632,
    682957958812161095,
    14099937096943718875,
    3537821847903334588,
    15308709719668994674,
    2794099073416336149,
    12927586219766552621,
    4746185978528512330,
    11868813884866820996,
    6197986026913258211,
    11300934188000567167,
    7953691094042902040,
    10018969513859038422,
    8772822531058113969,
    5972355155933334465,
    11553920467520400038,
    4988982520235124840,
    13225305222168267023,
    9033914876960820371,
    10370450306035609076,
    7674298433221668666,
    10967744966390550109,
    980624245289157989,
    18387692199238345730,
    1893091818053494476,
    16784995896345282475,
    2460962550551167543,
    15029299527363224400,
    3889249785709470110,
    14361047095730766073,
    7152808667132071294,
    10517653052196138009,
    8403046072361971415,
    9667057747487248304,
    5548181292602018348,
    13711997764589120331,
    6566640822083430789,
    12219586461562210530,
    4339799078486901722,
    14884331248692339389,
    3162771794102444147,
    15659499812784685332,
    1407067760449242248,
    16227380404614686191,
    313163830016681761,
    17792949120146540102,
    15974270316219011126,
    3388349708669235537,
    14586735073229501343,
    4097055683245593336,
    17441591293421672292,
    52124492066715139,
    16560446850825434317,
    1686407326344779178,
    9424331866618764946,
    8105397181042573301,
    10743213296764779835,
    7467632007823023196,
    12498908495905053120,
    6899759966970869927,
    13450976079457434217,
    5196770611440655118,
    3068694993950346357,
    15574544801069280530,
    4415864796418699228,
    14987303974132666043,
    372331187602115367,
    17843012212744579648,
    1365915917624322190,
    16159304030831938025,
    8498222161341154001,
    9753130120177886134,
    7075643695806669176,
    10413562999066574879,
    6508608486378259843,
    12170675365628437732,
    5588198146832672298,
    13778922177141081933,
    10693184289553322301,
    7408428365823553626,
    9492371957057024660,
    8146583108176139251,
    13535897005768955503,
    5290883696024090376,
    12395972053826516422,
    6823660164713125025,
    14635612084849909657,
    4155124302291582718,
    15907382188085804080,
    3348298769055669591,
    16474408562464413899,
    1591194954008525228,
    17545645062116227938,
    129323548791282181,
    11944710311866668930,
    6301088460921386725,
    12833674311484863531,
    4661096861331248460,
    9977965040470249680,
    8704629574110343607,
    11359949779118371705,
    8003866370626982430,
    18067829753921640742,
    578720537282459713,
    17105708341262678671,
    2294156538361666536,
    15348596866443337332,
    2861192775897808659,
    14042038867873840605,
    3488745859071548602,
    1961248490578315978,
    16826034455146343341,
    930483053039413603,
    18328640324767139844,
    3786183636106988952,
    14285116583335947519,
    2546017583431396913,
    15123247718981013334,
    4921925101102335086,
    13185383991055918345,
    6021397059357566919,
    11611854981016897184,
    7778499571418940220,
    11044827440017728091,
    8947724822199804053,
    10275350117751980530,
    14305617334264142588,
    3761093961753967515,
    15085298843839002965,
    2588562030682724402,
    16806092144723942830,
    1985788297180019913,
    18367165506942582279,
    887371421264944992,
    11096362585204036696,
    7731526995704526143,
    10241271927232264177,
    8977251455468689046,
    13133281644166861578,
    4969473966670574189,
    11645383286605688995,
    5992428849414869444,
    8679598156973803444,
    9998541640262035155,
    8046345929224184861,
    11321918423675127162,
    6325543588204888294,
    11924700913777369473,
    4618058879407596367,
    12872255551859819048,
    2814135520898484496,
    15400064923996039287,
    3518346142157433529,
    14008016735519820766,
    626327660033363522,
    18015803255862357797,
    2265123439798136299,
    17139154166583528588,
    5320387523016392971,
    13501796558728470636,
    6776699417338471074,
    12447518477596771269,
    7379474183244007001,
    10726726072749451070,
    8194111366491186672,
    9440249552670590103,
    1633717677315428271,
    16436438513133792968,
    104248984133430278,
    17566160372983778657,
    4112025581188892925,
    14674149627941317018,
    3372814652689558356,
    15887416504451402291,
    17808978818986351683,
    401919659527978276,
    16210794362085146602,
    1318881486081318541,
    15608010701067508497,
    3039682519820008054,
    14935264015646046392,
    4463457908986452447,
    12132629433066099431,
    6551072918100554624,
    13799519933941739854,
    5563188436460818473,
    9791734716584842677,
    8455208085205316818,
    10393541222881310236,
    7100085895238391675
};

        internal static readonly HashValueT[] g_arrPolynomialsTU = new HashValueT[]
        {
    0,
    4301801139292825854,
    8603602278585651708,
    5536388390095534338,
    17207204557171303416,
    15384107454405462790,
    11072776780191068676,
    11680497438901148410,
    3399477666658739863,
    1485712566272003689,
    6362315639424005995,
    7203627473650376597,
    13970477814449489263,
    18037491369955011985,
    13152544174998426771,
    10174801875925302381,
    6798955333317479726,
    7343590343088645584,
    2971425132544007378,
    1337443959955223596,
    12724631278848011990,
    10026677305644156456,
    14407254947300753194,
    18177596076388964308,
    8176253475373689785,
    5387697614824719175,
    437343424847641157,
    4441341839727792827,
    11509976169002698817,
    11819898501372141759,
    16779713916963445181,
    15235279240177103171,
    13597910666634959452,
    9729581611081462434,
    14687180686177291168,
    17320923730036663134,
    5942850265088014756,
    7622957599666950490,
    2674887919910447192,
    2210156069451563174,
    10635291588165958859,
    12117836404671616053,
    16500634784867953975,
    16090541995435050441,
    9030948952542371635,
    5109176964993923021,
    734722846184410831,
    3567224537215198769,
    16352506950747379570,
    15662630233491390348,
    10775395229649438350,
    12554616870618721904,
    874686849695282314,
    4003860967134960756,
    8882683679455585654,
    4681123366063000968,
    14826580613904347621,
    17758126382151194907,
    13449079118773674009,
    9302092035287386343,
    2526200408076685853,
    1782806131629947619,
    6082392029802611681,
    8060297690747382559,
    10078813272498312671,
    12712590950999424289,
    18210588723301041187,
    14342294302338362589,
    7283277331612612135,
    6818581215239986905,
    1330044672851732443,
    3010187741600223013,
    11885700530176029512,
    11475573106085141430,
    15245915199333900980,
    16728425381265001034,
    5349775839820894384,
    8182241796749178958,
    4420312138903126348,
    498504417201383858,
    15385878111726065905,
    17165065118103941135,
    11737431966813063437,
    11047520614915924467,
    4272187611567520521,
    70591564004791287,
    5489880589200304885,
    8619018972554314251,
    18061897905084743270,
    13914945456172553880,
    10218353929987846042,
    13149934332893136740,
    1469445692368821662,
    3447387087466427744,
    7134449074430397538,
    6391090532086621340,
    3993101813279920003,
    925852190704134013,
    4615198138264870527,
    8916963543387551361,
    15683255264648703099,
    16290941288717632645,
    12592133971661290887,
    10769002234304042361,
    1749373699390564628,
    2590721267719897578,
    8007721934269921512,
    6093992568036020246,
    17765367358911171308,
    14787659694496490002,
    9362246732126001936,
    13429294922205623278,
    7579810146560656045,
    5945864708141427283,
    2186154139572015953,
    2730824883429440431,
    9798883136271802709,
    13569258899950832043,
    17337313734206238889,
    14639394395644123223,
    5052400816153371706,
    9056363496880229572,
    3565612263259895238,
    777020668608421176,
    12164784059605223362,
    10620314748244324156,
    16120595381494765118,
    16430483079206223552,
    16504446506355919577,
    16048722403779633703,
    10551283116878519077,
    12240398199456904155,
    848884307675425057,
    3491633999998009823,
    8980729027659790557,
    5121406620485029923,
    14566554663225224270,
    17408063004841477296,
    13637162430479973810,
    9724397093499753804,
    2660089345703464886,
    2259004306325743432,
    6020375483200446026,
    7511928049811158708,
    13497621773598509047,
    9287053712205917961,
    14715245481915323915,
    17835411851473026805,
    6169203740848382991,
    7939418733438307569,
    2520688326786266611,
    1821804961067621645,
    10699551679641788768,
    12668450690017839518,
    16364483593498357916,
    15612082666466580578,
    8840624277806252696,
    4684629444214483558,
    997008834402767716,
    3919546852594924442,
    6466862386615521413,
    7065576394715494523,
    3375671824445292921,
    1543568071152642439,
    13218817467853321085,
    10142595829333711747,
    13840844523555971713,
    18133637912701274751,
    8544375223135041042,
    5557625176154676972,
    141183128009582574,
    4199188935557074704,
    10979761178400609770,
    11812066364189457684,
    17238037945108628502,
    15315266196300688616,
    568672677564210603,
    4348020457178674517,
    8107172544646843479,
    5418225222053717161,
    16800697827654303315,
    15175724405212419757,
    11407111089982549935,
    11960753849783764817,
    2938891384737643324,
    1403464455908840386,
    6894774174932855488,
    7213704255209964094,
    14268898148860795076,
    18281903212161724474,
    12782181064173242680,
    10002631852062286278,
    7986203626559840006,
    6154064139114412024,
    1851704381408268026,
    2450382655405204996,
    9230396276529741054,
    13523155031101002752,
    17833927086775102722,
    14757670813596976636,
    4754093731753785745,
    8812135273471349103,
    3936090823020423277,
    949376510125785235,
    12625184524284058217,
    10702447410067452567,
    15587953227866508181,
    16420293048296563563,
    3498747398781129256,
    809835810290449110,
    5181442535439795156,
    8960826049207626538,
    16015443868539843024,
    16569121262998985006,
    12187985136072040492,
    10563046348204787922,
    2279756914923058367,
    2598651261113982017,
    7549563932200691011,
    6014101269202275773,
    17397149953449989959,
    14617566106697367481,
    9658309172072138427,
    13671279600782192197,
    15159620293121312090,
    16848770010274327972,
    11891729416282854566,
    11436039948364495960,
    4372308279144031906,
    513021606122638940,
    5461649766858880862,
    8104435193283911584,
    18252126922087457741,
    14339326950516362035,
    9955970084911133233,
    12797443791885720271,
    1405353825856929845,
    2896870658298030283,
    7270766291842404809,
    6869645518378498359,
    10104801632306743412,
    13224933367205929098,
    18112726993760459144,
    13902124297793827190,
    7131224526519790476,
    6432305426698109810,
    1554041337216842352,
    3324220595653989006,
    11751625775273422563,
    10999259482882852381,
    15307748127850233631,
    17276681772817618913,
    5609915040545279259,
    8532488792823162341,
    4232344276098637031,
    76385176676938777
};
    }
}
